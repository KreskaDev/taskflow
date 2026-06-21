using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.TaskManagement.Events;

namespace TaskFlow.Application.TaskManagement.Commands;

/// <summary>
/// Handles the deferred-reaper message <see cref="ReapDeletedTask"/> off the durable
/// <c>task-reaper</c> local queue (Program.cs): it HARD-deletes the physical row a previous
/// soft-delete scheduled for erasure. This is queue infrastructure with NO caller — it is excluded
/// from the deny-by-default authorization predicate in <c>Program.cs</c> (alongside
/// <c>AccountDeletionRequested</c>), so it injects no <c>ICurrentUser</c> and loads by raw id
/// (owner-agnostic, tombstone-inclusive) rather than owner-scoped.
/// </summary>
/// <remarks>
/// Idempotent AND restore-aware. The row is erased ONLY when it is still the exact same tombstone the
/// message scheduled — the guard is three conjoined conditions:
/// <list type="bullet">
/// <item>the row STILL exists (a prior reaper delivery, or a hard-delete cascade, may have removed it
/// already) — else no-op;</item>
/// <item><c>deleted_at</c> is NON-null (a slice-014 restore CLEARS it) — else no-op, the restore wins;</item>
/// <item><c>deleted_at</c> EQUALS the scheduled <see cref="ReapDeletedTask.DeletedAtInstant"/> (a
/// re-delete after restore stamps a NEW instant) — else no-op, this message is stale.</item>
/// </list>
/// The instant comparison is at MICROSECOND resolution: Postgres <c>timestamptz</c> stores 6 fractional
/// digits, but the message instant carries full .NET 100ns ticks (7 digits) through the outbox JSON, so
/// a reloaded <c>deleted_at</c> is truncated relative to the message value. An exact tick <c>==</c> would
/// almost always mismatch and silently leak every tombstone; truncating both operands to whole
/// microseconds makes a genuinely-unchanged tombstone compare equal while still letting any real restore
/// (null, or a distinct instant) win the race.
/// <para>
/// The hard-delete is wrapped so an interleaved restore/re-delete that slips between the load and the
/// commit cannot fail the message: the <c>version</c> EF concurrency token makes a 0-rows-affected DELETE
/// surface as a <c>DbUpdateConcurrencyException</c>, which the repository translates to
/// <see cref="VersionConflictException"/> — for the reaper that simply means "someone else changed the
/// row first", i.e. another no-op, not a failure.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-001 AccountDeletionRequestedHandler / slice-002 CreateTaskHandler).")]
public static class ReapDeletedTaskHandler
{
    private const long TicksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;

    public static async Task Handle(
        ReapDeletedTask message,
        ITaskRepository tasks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(tasks);

        var task = await tasks
            .FindByIdIncludingDeletedAsync(message.TaskId, cancellationToken)
            .ConfigureAwait(false);

        // Row already gone (prior reaper delivery, or cascade-erased with its owning user) → nothing to do.
        if (task is null)
        {
            return;
        }

        // A slice-014 restore CLEARED the tombstone, or a re-delete stamped a DIFFERENT instant → the
        // restore/re-delete wins; this scheduled erasure is stale, so no-op (compared at µs resolution).
        if (task.DeletedAt is not { } deletedAt || !SameInstant(deletedAt, message.DeletedAtInstant))
        {
            return;
        }

        tasks.Remove(task);
        try
        {
            await tasks.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (VersionConflictException)
        {
            // Interleaved-race backstop: a restore/re-delete changed the row's `version` between the load
            // and this commit, so the DELETE affected 0 rows (translated from EF's
            // DbUpdateConcurrencyException at the persistence seam). For the reaper that just means the
            // row was no longer the tombstone we were erasing — swallow it as another idempotent no-op.
        }
    }

    /// <summary>
    /// Compares two instants at whole-MICROSECOND resolution (Postgres <c>timestamptz</c> precision), so a
    /// message instant carrying full .NET 100ns ticks still equals the same instant reloaded (and truncated)
    /// from the database. Tick-exact <c>==</c> would otherwise spuriously mismatch.
    /// </summary>
    private static bool SameInstant(DateTime a, DateTime b) =>
        a.Ticks / TicksPerMicrosecond == b.Ticks / TicksPerMicrosecond;
}
