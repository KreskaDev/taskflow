using System.Diagnostics.CodeAnalysis;
using TaskFlow.Domain.TaskManagement.Events;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Handles the deferred-reaper message <see cref="ReapDeletedComment"/> off the durable
/// <c>comment-reaper</c> local queue (Program.cs): it HARD-purges the physical comment row (its owned
/// <c>comment_mentions</c> cascade with it) a previous soft-delete scheduled for erasure. Queue
/// infrastructure with NO caller — excluded from the deny-by-default authorization predicate in
/// <c>Program.cs</c> (alongside <c>ReapDeletedTask</c>/<c>AccountDeletionRequested</c>), so it injects no
/// <c>ICurrentUser</c> and loads by raw id (tombstone-inclusive). The <c>ReapDeletedTaskHandler</c> mirror.
/// </summary>
/// <remarks>
/// Idempotent AND restore-aware. The row is erased ONLY when it is still the exact same tombstone the
/// message scheduled:
/// <list type="bullet">
/// <item>the row STILL exists (a prior delivery, or the parent task's hard-delete cascade, may have removed
/// it) — else no-op;</item>
/// <item><c>deleted_at</c> is NON-null (a slice-014 restore CLEARS it) — else no-op, the restore wins;</item>
/// <item><c>deleted_at</c> EQUALS the scheduled <see cref="ReapDeletedComment.DeletedAtInstant"/> at
/// whole-MICROSECOND resolution (Postgres <c>timestamptz</c> keeps 6 fractional digits while the outbox
/// JSON carries full .NET 100ns ticks — an exact tick <c>==</c> would spuriously mismatch) — else no-op,
/// this message is stale (a re-delete stamps a NEW instant and schedules its own reaper).</item>
/// </list>
/// Comments are VERSIONLESS (R2), so there is no <c>Task.Version</c> 0-rows-DELETE concurrency backstop
/// analog — the instant match is the SOLE guard (research R5).
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors ReapDeletedTaskHandler).")]
public static class ReapDeletedCommentHandler
{
    private const long TicksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;

    public static async Task Handle(
        ReapDeletedComment message,
        ICommentRepository comments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(comments);

        var comment = await comments
            .FindByIdIncludingDeletedAsync(message.CommentId, cancellationToken)
            .ConfigureAwait(false);

        // Row already gone (prior delivery, or cascade-erased with its parent task) → nothing to do.
        if (comment is null)
        {
            return;
        }

        // A cleared tombstone (the slice-014 restore seam) or a re-stamped different instant → this
        // scheduled erasure is stale; the restore/re-delete wins (compared at µs resolution).
        if (comment.DeletedAt is not { } deletedAt || !SameInstant(deletedAt, message.DeletedAtInstant))
        {
            return;
        }

        comments.Remove(comment);
        await comments.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Compares two instants at whole-MICROSECOND resolution (Postgres <c>timestamptz</c> precision), so the
    /// message instant carrying full .NET ticks still equals the same instant reloaded (truncated) from the
    /// database — the <c>ReapDeletedTaskHandler.SameInstant</c> mirror.
    /// </summary>
    private static bool SameInstant(DateTime a, DateTime b) =>
        a.Ticks / TicksPerMicrosecond == b.Ticks / TicksPerMicrosecond;
}
