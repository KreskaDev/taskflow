using TaskFlow.Domain.IdentityAccess;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Persistence seam for the <see cref="TaskEntity"/> aggregate. Defined in the Application layer
/// (and implemented in Infrastructure over EF Core) so handlers never depend on the
/// persistence technology directly (clean-architecture dependency direction).
/// </summary>
public interface ITaskRepository
{
    /// <summary>
    /// Finds the caller's NON-deleted task by id (owner-scoped + <c>deleted_at IS NULL</c>), or
    /// <c>null</c> if no such row exists. The generic single-row load for rename/status/reorder/get —
    /// a foreign, absent, or soft-deleted id all resolve to <c>null</c> (the handler maps that to 404).
    /// </summary>
    Task<TaskEntity?> FindOwnedAsync(TaskId id, UserId owner, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the caller's task by id WITHOUT the <c>deleted_at IS NULL</c> filter — owner-scoped but
    /// TOMBSTONE-INCLUSIVE. Used ONLY by DELETE so the handler can distinguish an own already-soft-deleted
    /// row (→ idempotent 204 no-op) from a foreign/absent id (→ 404); returns <c>null</c> only when no row
    /// owned by <paramref name="owner"/> exists at all.
    /// </summary>
    Task<TaskEntity?> FindOwnedIncludingDeletedAsync(TaskId id, UserId owner, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the caller's NON-deleted tasks (owner-scoped + <c>deleted_at IS NULL</c>), ordered by
    /// <c>position</c> then <c>id</c> (the canonical newest-first list query, FR-007).
    /// </summary>
    Task<IReadOnlyList<TaskEntity>> ListOwnedAsync(UserId owner, CancellationToken cancellationToken);

    /// <summary>Stages a newly created task for insertion.</summary>
    void Add(TaskEntity task);

    /// <summary>Commits staged changes to the database.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
