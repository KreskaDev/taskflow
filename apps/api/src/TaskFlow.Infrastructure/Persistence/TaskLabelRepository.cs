using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaskFlow.Application.Errors;
using TaskFlow.Application.TaskManagement.Labels;
using TaskFlow.Domain.IdentityAccess;
using LabelId = TaskFlow.Domain.TaskManagement.LabelId;
using TaskLabel = TaskFlow.Domain.TaskManagement.TaskLabel;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ITaskLabelRepository"/> over <see cref="AppDbContext"/> — the
/// <c>task_labels</c> relation (data-model R2). Every query joins to <c>labels</c> and filters
/// <c>owner_id = caller</c>, so it is strictly caller-scoped (per-user isolation): another member's labels
/// on a shared task are never read or written.
/// </summary>
public sealed class TaskLabelRepository(AppDbContext db) : ITaskLabelRepository
{
    public async Task SetForOwnerAsync(TaskId taskId, UserId owner, IReadOnlyCollection<LabelId> labelIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(labelIds);

        // The caller's CURRENT applications on this task (caller-scoped via the labels join). Materialized so
        // the add/remove delta is computed in memory (no Contains over a value-converted id in the remove path).
        var callerRows = await (
            from tl in db.TaskLabels
            join l in db.Labels on tl.LabelId equals l.Id
            where tl.TaskId == taskId && l.OwnerId == owner
            select tl).ToListAsync(cancellationToken).ConfigureAwait(false);

        var currentSet = callerRows.Select(r => r.LabelId).ToHashSet();
        var desiredSet = labelIds.ToHashSet();

        var rowsToRemove = callerRows.Where(r => !desiredSet.Contains(r.LabelId)).ToList();
        var idsToAdd = desiredSet.Where(id => !currentSet.Contains(id)).ToList();
        if (rowsToRemove.Count == 0 && idsToAdd.Count == 0)
        {
            return; // idempotent no-op: same set → no write.
        }

        db.TaskLabels.RemoveRange(rowsToRemove);
        foreach (var labelId in idsToAdd)
        {
            db.TaskLabels.Add(new TaskLabel(taskId, labelId));
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // A label in the desired set was concurrently deleted between the handler's ownership validation
            // and this insert → the task_labels.label_id FK violates. Detach EVERY pending row (not just the
            // rejected one) — a failed SaveChanges rolls back the whole batch but leaves the sibling
            // adds/removes tracked, which Wolverine's AutoApplyTransactions commit-flush would re-attempt →
            // an uncaught 500. Then signal the handler to map this to the same recoverable 422 the ownership
            // pre-check yields (the label is no longer a valid target).
            DetachPendingTaskLabels();

            throw new DuplicateLabelException("A referenced label no longer exists.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent apply of the SAME (task, label) won the race and already reached the desired
            // "present" state (PK_task_labels). The per-user set-replace is idempotent, so this is a benign
            // no-op. Detach EVERY pending row — NOT just ex.Entries: the failed SaveChanges rolled back the
            // whole batch, so any sibling add/remove from a MIXED set-replace is still tracked and Wolverine's
            // commit-flush would re-attempt it (a 0-row delete or a duplicate insert → an uncaught 500, or a
            // silent partial write after a 200 was returned). The client's onSettled invalidate reconciles to
            // the authoritative set.
            DetachPendingTaskLabels();
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent set-replace (or a DeleteLabel FK cascade) already removed a row this call also
            // tried to remove → a 0-rows-affected delete. Same handling as the unique-violation swallow:
            // detach EVERY pending row so the commit-flush cannot re-attempt a sibling, then treat as a benign
            // idempotent no-op reconciled by the client's onSettled invalidate.
            DetachPendingTaskLabels();
        }
    }

    /// <summary>
    /// Detaches every pending <see cref="TaskLabel"/> add/remove from the change tracker. Called on the race
    /// catch paths: a failed <c>SaveChanges</c> rolls back the batch but leaves its entities tracked, and
    /// Wolverine's commit-time re-flush (AutoApplyTransactions) would otherwise re-attempt them — re-raising
    /// the same conflict OUTSIDE any catch (→ 500). Detaching the whole pending delta (not just the rejected
    /// row) keeps a MIXED add+remove set-replace safe under a race.
    /// </summary>
    private void DetachPendingTaskLabels()
    {
        foreach (var entry in db.ChangeTracker.Entries<TaskLabel>()
            .Where(e => e.State is EntityState.Added or EntityState.Deleted)
            .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    public async Task<IReadOnlyList<Guid>> ListLabelIdsForTaskAsync(TaskId taskId, UserId owner, CancellationToken cancellationToken) =>
        await (
            from tl in db.TaskLabels
            join l in db.Labels on tl.LabelId equals l.Id
            where tl.TaskId == taskId && l.OwnerId == owner
            select tl.LabelId.Value).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<TaskId, IReadOnlyList<Guid>>> ListLabelIdsForTasksAsync(
        IReadOnlyCollection<TaskId> taskIds, UserId owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(taskIds);
        if (taskIds.Count == 0)
        {
            return new Dictionary<TaskId, IReadOnlyList<Guid>>();
        }

        // ONE batched caller-scoped join (R6). `taskIds.Contains(tl.TaskId)` is a Contains over the NON-nullable
        // value-converted TaskId → translates (the slice-005 trap is the NULLABLE-FK case; precedent:
        // ProjectRepository.ListByIdsAsync). Verified by the LabelMappingTests spike.
        var rows = await (
            from tl in db.TaskLabels
            join l in db.Labels on tl.LabelId equals l.Id
            where l.OwnerId == owner && taskIds.Contains(tl.TaskId)
            select new { tl.TaskId, LabelId = tl.LabelId.Value })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows
            .GroupBy(r => r.TaskId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(x => x.LabelId).ToList());
    }
}
