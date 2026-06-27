using TaskFlow.Domain.IdentityAccess;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;

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
    /// Lists the caller's Inbox — NON-deleted, UNPROJECTED tasks (owner-scoped + <c>deleted_at IS NULL AND
    /// project_id IS NULL</c>), ordered by <c>position</c> then <c>id</c> (FR-021/R6, slice 004). Narrowed
    /// from slice-002's flat list; backward-compatible because every pre-004 task has no project.
    /// </summary>
    Task<IReadOnlyList<TaskEntity>> ListOwnedAsync(UserId owner, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a task by id ALONE — NOT owner-scoped and TOMBSTONE-INCLUSIVE (no <c>deleted_at IS NULL</c>
    /// filter). Used ONLY by the deferred-reaper (<c>ReapDeletedTask</c>), which is queue infrastructure
    /// with no caller/owner, so it must load the candidate tombstone by its raw id to confirm the row is
    /// still the same soft-deleted instant before hard-deleting it. Returns <c>null</c> if no row exists.
    /// </summary>
    Task<TaskEntity?> FindByIdIncludingDeletedAsync(TaskId id, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the NON-deleted tasks of <paramref name="projectId"/> owned by <paramref name="owner"/>
    /// (owner-scoped + <c>deleted_at IS NULL AND project_id = {id}</c>), ordered by <c>position</c> then
    /// <c>id</c> — the project's task list (slice 004, R6) and the source for the delete <c>cascade</c> task
    /// disposition (R5: each loaded task is soft-deleted via the domain method, version-bumping).
    /// </summary>
    Task<IReadOnlyList<TaskEntity>> ListByProjectAsync(ProjectId projectId, UserId owner, CancellationToken cancellationToken);

    /// <summary>
    /// Counts the NON-deleted tasks of <paramref name="projectId"/> owned by <paramref name="owner"/> — the
    /// cross-row fact that makes <c>taskDisposition</c> REQUIRED on delete (FR-014/EC-03: a project WITH tasks
    /// must carry a disposition). Owner-scoped + <c>deleted_at IS NULL</c>.
    /// </summary>
    Task<int> CountByProjectAsync(ProjectId projectId, UserId owner, CancellationToken cancellationToken);

    /// <summary>Stages a newly created task for insertion.</summary>
    void Add(TaskEntity task);

    /// <summary>Stages an existing task for HARD deletion (physical row removal).</summary>
    void Remove(TaskEntity task);

    /// <summary>Commits staged changes to the database.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
