using Cycle = TaskFlow.Domain.TaskManagement.Cycle;
using CycleId = TaskFlow.Domain.TaskManagement.CycleId;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;

namespace TaskFlow.Application.TaskManagement.Cycles;

/// <summary>
/// Per-cycle task counts for the computed team-wide metrics (D6): aggregated over ALL non-deleted
/// tasks assigned to the cycle, regardless of author (D10 — the numbers are team-wide).
/// </summary>
public sealed record CycleTaskCounts(int Backlog, int Todo, int InProgress, int Done, int Cancelled)
{
    /// <summary>All non-deleted tasks in the cycle.</summary>
    public int Total => Backlog + Todo + InProgress + Done + Cancelled;

    /// <summary>The all-zero counts for a cycle with no tasks.</summary>
    public static CycleTaskCounts Empty { get; } = new(0, 0, 0, 0, 0);
}

/// <summary>
/// Persistence seam for the <see cref="Cycle"/> aggregate (slice 011). Defined in the Application
/// layer (implemented in Infrastructure over EF Core) so handlers never depend on the persistence
/// technology directly. Cycles are TEAM-WIDE — no read here is caller-scoped (spec IX); the
/// caller-visibility filtering of task ROWS lives in the query handler (D10).
/// </summary>
public interface ICycleRepository
{
    /// <summary>Finds a cycle by id, or null. Cycles are never soft-deleted (hard delete only).</summary>
    Task<Cycle?> FindByIdAsync(CycleId id, CancellationToken cancellationToken);

    /// <summary>Lists ALL cycles in the D5 order <c>(StartDate, CreatedAt, Id)</c>.</summary>
    Task<IReadOnlyList<Cycle>> ListAllAsync(CancellationToken cancellationToken);

    /// <summary>The single active cycle, or null (the D3 invariant guarantees at most one).</summary>
    Task<Cycle?> FindActiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The rollover "next cycle": the PLANNED cycle lowest by <c>(StartDate, CreatedAt, Id)</c>
    /// (D5), or null when none exists (→ <c>no_next_cycle</c>, US-05.AS-06).
    /// </summary>
    Task<Cycle?> FindNextPlannedAsync(CancellationToken cancellationToken);

    /// <summary>Whether a cycle row with <paramref name="id"/> exists (the SetTaskCycle 422 guard).</summary>
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Counts ALL task rows referencing <paramref name="cycleId"/> — INCLUDING soft-deleted
    /// tombstones, deliberately: the FK RESTRICT backstop counts physical rows, so the friendly
    /// <c>cycle_not_empty</c> guard must agree with it or an "empty" delete would 500 on the FK.
    /// </summary>
    Task<int> CountTasksReferencingAsync(Guid cycleId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the cycle's NON-deleted tasks (team-wide, no caller filter — D10 filtering happens in
    /// the handler), ordered by <c>position</c> then <c>id</c>. Serves GetCycleTasks and the
    /// CloseCycle rollover walk.
    /// </summary>
    Task<IReadOnlyList<TaskEntity>> ListTasksInCycleAsync(Guid cycleId, CancellationToken cancellationToken);

    /// <summary>
    /// The metrics source (D6): per-cycle, per-status counts over ALL non-deleted assigned tasks,
    /// computed in ONE grouped count query (no N+1). Cycles with no tasks are absent from the map
    /// (use <see cref="CycleTaskCounts.Empty"/>).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, CycleTaskCounts>> CountTasksByCycleAsync(CancellationToken cancellationToken);

    /// <summary>Stages a newly created cycle for insertion.</summary>
    void Add(Cycle cycle);

    /// <summary>Stages an existing cycle for HARD deletion (physical row removal).</summary>
    void Remove(Cycle cycle);

    /// <summary>Commits staged changes, translating provider errors to Application-layer signals.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
