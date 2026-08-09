using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.TaskManagement;
using TaskFlow.Domain.TaskManagement.Events;
using Wolverine;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Deletes the caller's OWN comment (contracts/openapi.yaml <c>deleteComment</c>, AS-04, research
/// R2/R4/R5) as a version-free, idempotent SOFT-delete: stamps <c>deleted_at</c> (the comment leaves the
/// thread immediately) and schedules a <c>ReapDeletedComment</c> (+30s) — the Constitution-VII 30s-undo
/// substrate mirroring <c>DeleteTask</c>/<c>ReapDeletedTask</c>. The user-facing restore is the slice-014
/// seam (NOT built). The caller is resolved from <see cref="ICurrentUser"/>, never the wire.
/// </summary>
/// <remarks>HTTP request bound by <c>DELETE /api/comments/{commentId}</c>; 204 on success AND on the
/// idempotent replay of the caller's own tombstone.</remarks>
public sealed record DeleteComment
{
    /// <summary>The comment identity (server-minted UUIDv7), carried in the route.</summary>
    public required CommentId Id { get; init; }
}

/// <summary>
/// Handles <see cref="DeleteComment"/> under the STRICT two-step gate (R4), on a tombstone-INCLUSIVE load
/// so the author-equality check still runs on an already-deleted row (the idempotent-replay path).
/// </summary>
/// <remarks>
/// Decision path:
/// <list type="bullet">
/// <item>tombstone-INCLUSIVE load (absent/foreign → 404 — the id space is no enumeration oracle).</item>
/// <item>STEP 1 — <see cref="CommentAccessGuards.LoadForCommentAsync"/> at
/// <see cref="EffectiveRole.Editor"/>: former member / non-member / now-personal → 404 (BEFORE the author
/// check — FR-066 &gt; FR-075); viewer (incl. a demoted author) → 403.</item>
/// <item>STEP 2 — author-equality: <c>AuthorId == caller</c> else 403 (incl. the project owner).</item>
/// <item>ONLY then branch on the tombstone: the caller's own ALREADY-deleted comment → idempotent 204
/// no-op (do NOT re-publish the reaper); a live row → <see cref="Comment.SoftDelete"/> + ONE scheduled
/// <see cref="ReapDeletedComment"/> (+30s) published to the outbox, committing together (R5).</item>
/// </list>
/// Versionless / LWW: no <c>version</c>, no 409 (R2).
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors DeleteTaskHandler).")]
public static class DeleteCommentHandler
{
    private static readonly TimeSpan ReaperDelay = TimeSpan.FromSeconds(30);

    public static async Task Handle(
        DeleteComment command,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        IResourceAuthorizationPolicy authorization,
        ICommentRepository comments,
        IMessageContext messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(comments);
        ArgumentNullException.ThrowIfNull(messages);

        // Tombstone-INCLUSIVE: the idempotent replay must find the caller's own already-deleted row.
        var comment = await comments.FindByIdIncludingDeletedAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (comment is null)
        {
            throw new NotFoundException();
        }

        // STEP 1 — the role floor on the parent shared project (live membership; R4/H1).
        await CommentAccessGuards
            .LoadForCommentAsync(comment.TaskId, EffectiveRole.Editor, currentUser, tasks, projects, members, authorization, cancellationToken)
            .ConfigureAwait(false);

        // STEP 2 — the object-level author grant (FR-075).
        if (comment.AuthorId != currentUser.Id)
        {
            throw new ForbiddenException();
        }

        // The caller's own already-tombstoned row: idempotent 204 no-op — no re-stamp, no second reaper.
        if (comment.DeletedAt is not null)
        {
            return;
        }

        // Soft-delete + the SCHEDULED reaper publish commit together in the per-message transaction; the
        // message carries the exact deleted_at instant so the reaper stays restore-aware (R5).
        var deletedAt = DateTime.UtcNow;
        comment.SoftDelete(deletedAt);

        await messages
            .PublishAsync(
                new ReapDeletedComment(comment.Id, deletedAt),
                new DeliveryOptions { ScheduleDelay = ReaperDelay })
            .ConfigureAwait(false);

        await comments.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
