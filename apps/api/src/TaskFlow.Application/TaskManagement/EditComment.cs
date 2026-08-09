using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Application.IdentityAccess;
using TaskFlow.Domain.TaskManagement;
using Wolverine;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Edits the caller's OWN comment (contracts/openapi.yaml <c>editComment</c>, AS-04, research R2/R4/R5/R7):
/// a WHOLE-body + WHOLE-mention-set replace under the strict two-step gate. LAST-WRITE-WINS — no
/// <c>version</c>, no 409 (R2). The caller is resolved from <see cref="ICurrentUser"/>, never the wire.
/// </summary>
/// <remarks>
/// HTTP request bound by <c>PATCH /api/comments/{commentId}</c>: <see cref="Id"/> from the route, the rest
/// from the body.
/// </remarks>
/// <summary>
/// The wire body for <c>PATCH /api/comments/{commentId}</c> (contracts/openapi.yaml
/// <c>EditCommentRequest</c>). The comment id is carried in the route; the caller is resolved from
/// <c>ICurrentUser</c>. WHOLE replace: the body overwrites the stored body and the mention list overwrites
/// the whole set (anti-silent-null). Named to match the OpenAPI schema component.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record EditCommentRequest
{
    /// <summary>The replacement body (required, not whitespace-only, ≤ 4000 — FR-098).</summary>
    public required string Body { get; init; }

    /// <summary>The replacement WHOLE mention set (each MUST be a current member — R6). Omitted/empty = none.</summary>
    public IReadOnlyList<Guid>? MentionedUserIds { get; init; }
}

public sealed record EditComment
{
    /// <summary>The comment identity (server-minted UUIDv7), carried in the route.</summary>
    public required CommentId Id { get; init; }

    /// <summary>The replacement body (trimmed-non-empty, ≤ <see cref="Comment.MaxBodyLength"/> — FR-098).</summary>
    public required string Body { get; init; }

    /// <summary>The replacement WHOLE mention set (anti-silent-null — R6).</summary>
    public required IReadOnlyList<Guid> MentionedUserIds { get; init; }
}

/// <summary>Validates <see cref="EditComment"/> at the boundary — the same content rules as post (FR-098).</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors PostCommentValidator).")]
public sealed class EditCommentValidator : AbstractValidator<EditComment>
{
    public EditCommentValidator()
    {
        RuleFor(x => x.Body)
            .Must(body => !string.IsNullOrWhiteSpace(body))
            .WithMessage("A comment body must not be empty or whitespace-only.")
            .Must(body => body is null || body.Trim().Length <= Comment.MaxBodyLength)
            .WithMessage($"A comment body must be at most {Comment.MaxBodyLength} characters.");

        RuleFor(x => x.MentionedUserIds)
            .NotNull()
            .Must(ids => ids is null || ids.Count <= PostCommentValidator.MaxMentions)
            .WithMessage($"A comment may mention at most {PostCommentValidator.MaxMentions} users.");
    }
}

/// <summary>
/// Handles <see cref="EditComment"/> under the STRICT two-step gate (R4): the slice-007 role floor FIRST,
/// author-equality SECOND — so FR-066 (membership loss revokes ALL) structurally beats FR-075 (the author
/// grant), and a demoted-to-viewer author fails the floor (403) before authorship is consulted (H1).
/// </summary>
/// <remarks>
/// Decision path:
/// <list type="bullet">
/// <item>live comment load (<c>deleted_at IS NULL</c>; absent/soft-deleted → 404).</item>
/// <item>STEP 1 — <see cref="CommentAccessGuards.LoadForCommentAsync"/> at
/// <see cref="EffectiveRole.Editor"/> on the parent: former member / non-member / now-personal → 404;
/// viewer → 403. Membership resolves LIVE here.</item>
/// <item>STEP 2 — author-equality: <c>AuthorId == caller</c> else 403 (a non-author of ANY role, including
/// the owner; a tombstoned author matches no caller).</item>
/// <item>mention candidacy re-validated (422), then <see cref="Comment.Edit"/> whole-replaces body +
/// mention set, stamps <c>edited_at</c>, and records the ADDED-only <c>UserMentioned</c> delta (R7).</item>
/// </list>
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors PostCommentHandler).")]
public static class EditCommentHandler
{
    public static async Task<CommentResponse> Handle(
        EditComment command,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        IResourceAuthorizationPolicy authorization,
        ICommentRepository comments,
        IUserRepository users,
        IMessageContext messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(comments);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(messages);

        var comment = await comments.FindByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (comment is null)
        {
            throw new NotFoundException();
        }

        // STEP 1 — the role floor on the parent shared project (live membership; R4/H1).
        var context = await CommentAccessGuards
            .LoadForCommentAsync(comment.TaskId, EffectiveRole.Editor, currentUser, tasks, projects, members, authorization, cancellationToken)
            .ConfigureAwait(false);

        // STEP 2 — the object-level author grant (FR-075): role does not override authorship.
        if (comment.AuthorId != currentUser.Id)
        {
            throw new ForbiddenException();
        }

        var mentions = CommentMentionRules.RequireCurrentMembers(command.MentionedUserIds, context);

        comment.Edit(command.Body, mentions, context.Project.Id, DateTime.UtcNow);
        await DomainEventDispatch.PublishAndClearAsync(comment, messages, cancellationToken).ConfigureAwait(false);
        await comments.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var displayNames = await CommentMentionRules
            .ResolveDisplayNamesAsync([comment], users, cancellationToken)
            .ConfigureAwait(false);
        return CommentResponse.From(comment, currentUser.Id, displayNames);
    }
}
