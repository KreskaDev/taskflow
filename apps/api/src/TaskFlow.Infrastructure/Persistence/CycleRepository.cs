using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaskFlow.Application.Errors;
using TaskFlow.Application.TaskManagement.Cycles;
using Cycle = TaskFlow.Domain.TaskManagement.Cycle;
using CycleId = TaskFlow.Domain.TaskManagement.CycleId;
using CycleStatus = TaskFlow.Domain.TaskManagement.CycleStatus;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskStatus = TaskFlow.Domain.TaskManagement.TaskStatus;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ICycleRepository"/> over <see cref="AppDbContext"/>. The
/// context is the Wolverine-integrated scoped DbContext, so writes participate in the per-message
/// transaction/outbox — which is exactly what makes the CloseCycle status-flip + task rollover a
/// single transaction (D4). All task-side filters use the RAW <c>Guid?</c> <c>CycleId</c> (D2 —
/// the value-converted-nullable-FK translation trap does not apply to an unconverted column).
/// </summary>
public sealed class CycleRepository(AppDbContext db) : ICycleRepository
{
    public Task<Cycle?> FindByIdAsync(CycleId id, CancellationToken cancellationToken) =>
        db.Cycles.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Cycle>> ListAllAsync(CancellationToken cancellationToken) =>
        await OrderedCycles()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<Cycle?> FindActiveAsync(CancellationToken cancellationToken) =>
        db.Cycles.FirstOrDefaultAsync(c => c.Status == CycleStatus.Active, cancellationToken);

    public Task<Cycle?> FindNextPlannedAsync(CancellationToken cancellationToken) =>
        OrderedCycles()
            .Where(c => c.Status == CycleStatus.Planned)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Cycles.AnyAsync(c => c.Id == CycleId.From(id), cancellationToken);

    public Task<int> CountTasksReferencingAsync(Guid cycleId, CancellationToken cancellationToken) =>
        // Tombstone-INCLUSIVE, deliberately: the FK RESTRICT backstop counts physical rows, so the
        // friendly cycle_not_empty guard must agree with it (a soft-deleted row still references).
        db.Tasks.CountAsync(t => t.CycleId == cycleId, cancellationToken);

    public async Task<IReadOnlyList<TaskEntity>> ListTasksInCycleAsync(Guid cycleId, CancellationToken cancellationToken) =>
        await db.Tasks
            .Where(t => t.CycleId == cycleId && t.DeletedAt == null)
            .OrderBy(t => t.Position)
            .ThenBy(t => t.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<Guid, CycleTaskCounts>> CountTasksByCycleAsync(CancellationToken cancellationToken)
    {
        // ONE grouped count query (D6): per-(cycle, status) counts over non-deleted assigned
        // tasks; composed into the per-cycle breakdown in memory (≤ 5 rows per cycle).
        var rows = await db.Tasks
            .Where(t => t.DeletedAt == null && t.CycleId != null)
            .GroupBy(t => new { t.CycleId, t.Status })
            .Select(g => new { g.Key.CycleId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(r => r.CycleId!.Value)
            .ToDictionary(
                g => g.Key,
                g => new CycleTaskCounts(
                    Backlog: g.Where(r => r.Status == TaskStatus.Backlog).Sum(r => r.Count),
                    Todo: g.Where(r => r.Status == TaskStatus.Todo).Sum(r => r.Count),
                    InProgress: g.Where(r => r.Status == TaskStatus.InProgress).Sum(r => r.Count),
                    Done: g.Where(r => r.Status == TaskStatus.Done).Sum(r => r.Count),
                    Cancelled: g.Where(r => r.Status == TaskStatus.Cancelled).Sum(r => r.Count)));
    }

    public void Add(Cycle cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        db.Cycles.Add(cycle);
    }

    public void Remove(Cycle cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        db.Cycles.Remove(cycle);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // `version` is an EF concurrency token (CycleConfiguration): an interleaved write that
            // changed it between the handler's version-compare and its commit produces a
            // 0-rows-affected UPDATE. Translate at the persistence seam (the TaskRepository
            // pattern) so handlers stay free of EF dependencies.
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            throw new VersionConflictException(
                "The cycle was modified by another request; reload and retry.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg)
        {
            // The new aggregate needs its DbUpdateException translation or the documented behavior
            // is a lie → 500 (the slice-006 lesson). DETACH first so Wolverine's
            // AutoApplyTransactions commit-flush cannot re-attempt the rejected write.
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            Exception? translated = pg switch
            {
                // ix_cycles_single_active: a racing second activation slipped past the handler
                // check — the D3 index wins; surface the same friendly 409 the check produces.
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_cycles_single_active" } =>
                    new CycleActiveConflictException("Another cycle was activated concurrently.", ex),
                // PK unique violation: a concurrent same-id PUT; the create handler re-resolves
                // the race to the idempotent 200.
                { SqlState: PostgresErrorCodes.UniqueViolation } =>
                    new DuplicateCycleIdException("A cycle with this id already exists.", ex),
                // tasks.cycle_id FK RESTRICT: a task was assigned concurrently with the delete —
                // the handler's emptiness pre-check has a TOCTOU window the FK closes.
                { SqlState: PostgresErrorCodes.ForeignKeyViolation } =>
                    new CycleNotEmptyException("A task was assigned to the cycle concurrently.", ex),
                _ => null,
            };

            if (translated is null)
            {
                throw;
            }

            throw translated;
        }
    }

    private IOrderedQueryable<Cycle> OrderedCycles() =>
        // The D5 deterministic order every surface agrees on: (StartDate, CreatedAt, Id).
        db.Cycles
            .OrderBy(c => c.StartDate)
            .ThenBy(c => c.CreatedAt)
            .ThenBy(c => c.Id);
}
