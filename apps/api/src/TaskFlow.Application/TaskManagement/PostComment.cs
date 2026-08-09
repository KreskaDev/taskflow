using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using FluentValidation.Results;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.IdentityAccess;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;
using Wolverine;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Posts a comment on a shared-project task (contracts/openapi.yaml <c>postTaskComment</c>, AS-01/AS-02,
/// research R3/R5/R6/R7/R8). The server MINTS the <c>CommentId</c> (UUIDv7 — never client-supplied, R1);
/// the caller is resolved from <see cref="ICurrentUser"/>, never the wire.
/// </summary>
/// <remarks>
/// HTTP request bound by <c>POST /api/tasks/{taskId}/comments</c>: <see cref="TaskId"/> from the route, the
/// rest from the body. Editor/owner-only on the parent SHARED project (viewer → 403; non-member / personal
/// task / foreign → 404 — the reused slice-007 dispatch via <see cref="CommentAccessGuards"/>); every
/// mention id must be a CURRENT member (else 422, no comment created); a genuine mention raises ONE
/// <c>UserMentioned</c> (the set minus self) through the outbox.
/// </remarks>
/// <summary>
/// The wire body for <c>POST /api/tasks/{taskId}/comments</c> (contracts/openapi.yaml
/// <c>PostCommentRequest</c>). The task id is carried in the route; the caller is resolved from
/// <c>ICurrentUser</c>. Named to match the OpenAPI schema component so the auto-emitted client schema
/// stays aligned.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record PostCommentRequest
{
    /// <summary>The comment text (required, not whitespace-only, ≤ 4000 — FR-098).</summary>
    public required string Body { get; init; }

    /// <summary>The typed @mention set (each MUST be a current member — R6). Omitted/empty = no mentions.</summary>
    public IReadOnlyList<Guid>? MentionedUserIds { get; init; }
}

public sealed record PostComment
{
    /// <summary>The parent task identity, carried in the route.</summary>
    public required TaskId TaskId { get; init; }

    /// <summary>The comment text (trimmed-non-empty, ≤ <see cref="Comment.MaxBodyLength"/> — FR-098).</summary>
    public required string Body { get; init; }

    /// <summary>The typed @mention set (structured User-id tokens, never scraped from prose — R6).</summary>
    public required IReadOnlyList<Guid> MentionedUserIds { get; init; }
}

/// <summary>
/// Validates <see cref="PostComment"/> at the boundary (FR-098): <c>Body</c> required, not
/// whitespace-only, ≤ <see cref="Comment.MaxBodyLength"/>; the mention list non-null with a sane cap. The
/// cross-row mention-candidacy check (current members only) lives in the handler. A violation →
/// <c>422 validation_failed</c>.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors slice-008 SetTaskAssigneesValidator).")]
public sealed class PostCommentValidator : AbstractValidator<PostComment>
{
    /// <summary>A sane mention cap for a ~10-person team's shared project (ASM-10).</summary>
    internal const int MaxMentions = 50;

    public PostCommentValidator()
    {
        RuleFor(x => x.Body)
            .Must(body => !string.IsNullOrWhiteSpace(body))
            .WithMessage("A comment body must not be empty or whitespace-only.")
            .Must(body => body is null || body.Trim().Length <= Comment.MaxBodyLength)
            .WithMessage($"A comment body must be at most {Comment.MaxBodyLength} characters.");

        RuleFor(x => x.MentionedUserIds)
            .NotNull()
            .Must(ids => ids is null || ids.Count <= MaxMentions)
            .WithMessage($"A comment may mention at most {MaxMentions} users.");
    }
}

/// <summary>
/// Handles <see cref="PostComment"/> (research R3/R5/R6/R7). Authentication is enforced upstream by the
/// deny-by-default middleware; this handler owns the reused-policy dispatch + mention candidacy + mint +
/// event drain.
/// </summary>
/// <remarks>
/// Decision path:
/// <list type="bullet">
/// <item><see cref="CommentAccessGuards.LoadForCommentAsync"/> at <see cref="EffectiveRole.Editor"/>
/// (personal/Inbox or foreign task → 404; shared viewer → 403; non-member → 404).</item>
/// <item>mention candidacy: every id MUST be a current member (the membership rows ∪ the owner anchor) —
/// else <see cref="ValidationException"/> → 422, NO comment created (R6).</item>
/// <item><see cref="Comment.Post"/> mints the id, de-dups the mentions, records <c>UserMentioned</c> for
/// the set minus self (R7); the event drains to the outbox and commits WITH the insert.</item>
/// </list>
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-008 SetTaskAssigneesHandler).")]
public static class PostCommentHandler
{
    public static async Task<CommentResponse> Handle(
        PostComment command,
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

        var context = await CommentAccessGuards
            .LoadForCommentAsync(command.TaskId, EffectiveRole.Editor, currentUser, tasks, projects, members, authorization, cancellationToken)
            .ConfigureAwait(false);

        var mentions = CommentMentionRules.RequireCurrentMembers(command.MentionedUserIds, context);

        var comment = Comment.Post(
            command.TaskId, context.Project.Id, currentUser.Id, command.Body, mentions, DateTime.UtcNow);

        await comments.AddAsync(comment, cancellationToken).ConfigureAwait(false);
        await DomainEventDispatch.PublishAndClearAsync(comment, messages, cancellationToken).ConfigureAwait(false);
        await comments.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var displayNames = await CommentMentionRules
            .ResolveDisplayNamesAsync([comment], users, cancellationToken)
            .ConfigureAwait(false);
        return CommentResponse.From(comment, currentUser.Id, displayNames);
    }
}

/// <summary>
/// The shared mention-candidacy + display-name plumbing of the comment verticals (R6/R15): candidacy is
/// validated against the CURRENT member roster (memberships ∪ the owner anchor) and read-side names resolve
/// tombstone-safe.
/// </summary>
internal static class CommentMentionRules
{
    /// <summary>
    /// Maps the wire mention ids to <see cref="UserId"/>s, de-duplicated, requiring every one to be a
    /// CURRENT member of the resolved project — else <see cref="ValidationException"/> (422, field error on
    /// <c>mentionedUserIds</c>; no comment is created/updated — R6).
    /// </summary>
    public static IReadOnlyList<UserId> RequireCurrentMembers(
        IReadOnlyList<Guid> mentionedUserIds, CommentAccessContext context)
    {
        var memberIds = context.Memberships.Select(m => m.UserId).Append(context.Project.OwnerId).ToHashSet();
        var mentions = mentionedUserIds.Select(UserId.From).Distinct().ToList();
        if (mentions.Any(id => !memberIds.Contains(id)))
        {
            throw new ValidationException(
            [
                new ValidationFailure(
                    "mentionedUserIds", "Every mentioned user must be a current member of the project."),
            ]);
        }

        return mentions;
    }

    /// <summary>
    /// Batch-resolves the display names of every author + mention id across <paramref name="comments"/>
    /// (never emails — Constitution XI). Ids whose user row is gone simply stay unresolved and render as the
    /// tombstone via <see cref="CommentResponse.From"/>.
    /// </summary>
    public static async Task<IReadOnlyDictionary<UserId, string>> ResolveDisplayNamesAsync(
        IReadOnlyCollection<Comment> comments, IUserRepository users, CancellationToken cancellationToken)
    {
        var ids = comments
            .SelectMany(c => c.Mentions.Where(m => m.UserId.HasValue).Select(m => m.UserId!.Value))
            .Concat(comments.Where(c => c.AuthorId.HasValue).Select(c => c.AuthorId!.Value))
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<UserId, string>();
        }

        var rows = await users.ListByIdsAsync(ids, cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(u => u.Id, u => u.DisplayName);
    }
}
