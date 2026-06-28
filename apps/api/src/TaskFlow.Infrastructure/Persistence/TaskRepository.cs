using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaskFlow.Application.Errors;
using TaskFlow.Application.TaskManagement;
using TaskFlow.Domain.IdentityAccess;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;
using TaskStatus = TaskFlow.Domain.TaskManagement.TaskStatus;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ITaskRepository"/> over <see cref="AppDbContext"/>.
/// The context is the Wolverine-integrated scoped DbContext, so writes participate in the
/// per-message transaction/outbox.
/// </summary>
public sealed class TaskRepository(AppDbContext db) : ITaskRepository
{
    public Task<TaskEntity?> FindOwnedAsync(TaskId id, UserId owner, CancellationToken cancellationToken) =>
        db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.CreatedBy == owner && t.DeletedAt == null, cancellationToken);

    public Task<TaskEntity?> FindOwnedIncludingDeletedAsync(TaskId id, UserId owner, CancellationToken cancellationToken) =>
        db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.CreatedBy == owner, cancellationToken);

    public Task<TaskEntity?> FindByIdIncludingDeletedAsync(TaskId id, CancellationToken cancellationToken) =>
        db.Tasks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<TaskEntity?> FindByIdAsync(TaskId id, CancellationToken cancellationToken) =>
        // NOT owner-scoped (a shared-project task may be authored by the project owner, not the caller), but
        // tombstone-exclusive. The slice-005 dispatch-by-visibility guard applies authorization afterwards.
        db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt == null, cancellationToken);

    public async Task<IReadOnlyList<TaskEntity>> ListDueInRangeReadableAsync(
        UserId caller,
        IReadOnlyCollection<ProjectId> sharedProjectIds,
        DateTime? lowerInclusiveUtc,
        DateTime upperExclusiveUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sharedProjectIds);

        // The zone-free date/status/tombstone filter (the boundary math is already collapsed to UTC instants).
        var query = db.Tasks.Where(t =>
            t.DeletedAt == null
            && t.DueDate != null
            && t.DueDate < upperExclusiveUtc
            && (lowerInclusiveUtc == null || t.DueDate >= lowerInclusiveUtc)
            && t.Status != TaskStatus.Done
            && t.Status != TaskStatus.Cancelled);

        // The dispatch-by-visibility read scope (R6/R10): the caller's own tasks PLUS tasks in shared projects
        // the caller is a current member of. Built as an OR of per-id equalities rather than Contains/IN:
        // Npgsql cannot array-map a collection of the value-converted nullable ProjectId FK, but a per-id
        // `project_id = @p` equality translates cleanly (the proven pattern from MoveProjectTasksToInboxAsync).
        // An empty shared set degrades to owner-only.
        return await query
            .Where(BuildReadableVisibilityPredicate(caller, sharedProjectIds))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static System.Linq.Expressions.Expression<Func<TaskEntity, bool>> BuildReadableVisibilityPredicate(
        UserId caller, IReadOnlyCollection<ProjectId> sharedProjectIds)
    {
        var t = System.Linq.Expressions.Expression.Parameter(typeof(TaskEntity), "t");

        // t.CreatedBy == caller (the value converter applies to the captured-as-constant UserId).
        System.Linq.Expressions.Expression body = System.Linq.Expressions.Expression.Equal(
            System.Linq.Expressions.Expression.Property(t, nameof(TaskEntity.CreatedBy)),
            System.Linq.Expressions.Expression.Constant(caller, typeof(UserId)));

        if (sharedProjectIds.Count > 0)
        {
            var projectId = System.Linq.Expressions.Expression.Property(t, nameof(TaskEntity.ProjectId)); // ProjectId?
            foreach (var id in sharedProjectIds)
            {
                // || t.ProjectId == (ProjectId?)id
                var eq = System.Linq.Expressions.Expression.Equal(
                    projectId,
                    System.Linq.Expressions.Expression.Constant((ProjectId?)id, typeof(ProjectId?)));
                body = System.Linq.Expressions.Expression.OrElse(body, eq);
            }
        }

        return System.Linq.Expressions.Expression.Lambda<Func<TaskEntity, bool>>(body, t);
    }

    public async Task<IReadOnlyList<TaskEntity>> ListOwnedAsync(UserId owner, CancellationToken cancellationToken) =>
        // The Inbox (FR-021/R6): narrowed to project_id IS NULL — unprojected tasks only. Backward-compatible
        // because every pre-slice-004 task has no project, so existing data stays in the Inbox; only tasks
        // moved to a project (R7) drop out. Keeps slice-002's ORDER BY position, id (and its index).
        await db.Tasks
            .Where(t => t.CreatedBy == owner && t.DeletedAt == null && t.ProjectId == null)
            .OrderBy(t => t.Position)
            .ThenBy(t => t.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<TaskEntity>> ListByProjectAsync(ProjectId projectId, UserId owner, CancellationToken cancellationToken) =>
        await db.Tasks
            .Where(t => t.ProjectId == projectId && t.CreatedBy == owner && t.DeletedAt == null)
            .OrderBy(t => t.Position)
            .ThenBy(t => t.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<int> CountByProjectAsync(ProjectId projectId, UserId owner, CancellationToken cancellationToken) =>
        db.Tasks.CountAsync(t => t.ProjectId == projectId && t.CreatedBy == owner && t.DeletedAt == null, cancellationToken);

    public async Task<IReadOnlyList<TaskEntity>> ListAssignedToAsync(UserId assignee, CancellationToken cancellationToken) =>
        // The caller's assigned, active, non-deleted tasks across all projects. EF translates the owned
        // Assignees.Any(...) to an EXISTS over task_assignees; assignees auto-load (owned). The handler
        // applies the readable-shared membership filter + grouping in-memory (R6).
        await db.Tasks
            .Where(t => t.DeletedAt == null
                && t.Status != TaskStatus.Done
                && t.Status != TaskStatus.Cancelled
                && t.Assignees.Any(a => a.UserId == assignee))
            .OrderBy(t => t.Position)
            .ThenBy(t => t.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task ClearAssigneesForProjectAsync(ProjectId projectId, CancellationToken cancellationToken) =>
        // Bulk, event-free revoke-all for a project's tasks (unshare / delete-to-inbox, R5). Parameterized
        // raw SQL — a set-based delete over the owned join table (like the slice-004 ExecuteUpdate bulk ops).
        db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM task_assignees WHERE task_id IN (SELECT id FROM tasks WHERE project_id = {projectId.Value})",
            cancellationToken);

    public Task ClearAssigneesForUserInProjectAsync(ProjectId projectId, UserId userId, CancellationToken cancellationToken) =>
        // Bulk, event-free revoke of one user's assignments across a project's tasks (remove/leave, R5).
        db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM task_assignees ta USING tasks t WHERE ta.task_id = t.id AND t.project_id = {projectId.Value} AND ta.user_id = {userId.Value}",
            cancellationToken);

    public void Add(TaskEntity task)
    {
        ArgumentNullException.ThrowIfNull(task);
        db.Tasks.Add(task);
    }

    public void Remove(TaskEntity task)
    {
        ArgumentNullException.ThrowIfNull(task);
        db.Tasks.Remove(task);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // The `version` column is an EF concurrency token (TaskConfiguration), so an interleaved
            // write that changed it between this handler's version-compare and its commit produces a
            // 0-rows-affected UPDATE → DbUpdateConcurrencyException. Translate that EF-specific signal
            // into the Application-layer VersionConflictException (→ 409) at the persistence seam, so
            // the rename/status/reorder handlers stay free of any EF dependency (clean-architecture
            // dependency direction; mirrors the DuplicateTaskIdException translation below). This is the
            // interleaved-race backstop the in-handler version compare cannot close on its own (R4).
            throw new VersionConflictException(
                "The task was modified by another request; reload and retry.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The only unique constraint on `tasks` is the PK on `id` (no unique on position) —
            // so a 23505 here is a concurrent double-insert of the same client-generated id.
            // DETACH the rejected entity first: it is still tracked in the `Added` state, which would
            // (1) make the handler's re-resolve return this optimistic shadow via EF identity
            // resolution instead of the persisted row, and (2) cause Wolverine's per-message
            // transaction commit (AutoApplyTransactions) to re-attempt the INSERT → an uncaught 500.
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            // Translate the EF/Npgsql-specific exception into an Application-layer signal so the
            // CreateTask handler can re-resolve the race without depending on persistence types
            // (clean-architecture dependency direction; research R2 — "PK is the race backstop").
            throw new DuplicateTaskIdException("A task with this id already exists.", ex);
        }
    }
}
