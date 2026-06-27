using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaskFlow.Application.Errors;
using TaskFlow.Application.TaskManagement;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IProjectRepository"/> over <see cref="AppDbContext"/>.
/// The context is the Wolverine-integrated scoped DbContext, so writes participate in the
/// per-message transaction/outbox. Mirrors <see cref="TaskRepository"/>, including the
/// <see cref="DbUpdateConcurrencyException"/> → <see cref="VersionConflictException"/> and the
/// unique-violation translation at <see cref="SaveChangesAsync"/>.
/// </summary>
public sealed class ProjectRepository(AppDbContext db) : IProjectRepository
{
    public Task<Project?> FindOwnedAsync(ProjectId id, UserId owner, CancellationToken cancellationToken) =>
        db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == owner && p.DeletedAt == null, cancellationToken);

    public Task<Project?> FindOwnedIncludingDeletedAsync(ProjectId id, UserId owner, CancellationToken cancellationToken) =>
        db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == owner, cancellationToken);

    public async Task<IReadOnlyList<Project>> ListOwnedAsync(UserId owner, bool includeArchived, CancellationToken cancellationToken) =>
        await db.Projects
            .Where(p => p.OwnerId == owner
                && p.DeletedAt == null
                && (includeArchived ? p.ArchivedAt != null : p.ArchivedAt == null))
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Project>> ListChildrenAsync(ProjectId parentId, UserId owner, CancellationToken cancellationToken) =>
        await db.Projects
            .Where(p => p.ParentId == parentId && p.OwnerId == owner && p.DeletedAt == null)
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<int> OrphanChildrenAsync(ProjectId parentId, UserId owner, CancellationToken cancellationToken) =>
        // Owner-scoped set-null of parent_id (the orphan_to_top child disposition, AS-10/R5). Only ACTIVE
        // (non-archived) children are promoted — an archived child is hidden and re-homed by its own
        // unarchive (R9) / the FK on reap, matching ApplyChildDispositionAsync's active-only scope.
        // ExecuteUpdate bypasses the change-tracker, so the strongly-typed ProjectId? converter does NOT
        // apply — write the raw nullable Guid the store column holds. Runs in the per-message transaction.
        db.Projects
            .Where(p => p.ParentId == parentId && p.OwnerId == owner && p.DeletedAt == null && p.ArchivedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.ParentId, (ProjectId?)null), cancellationToken);

    public Task<int> MoveProjectTasksToInboxAsync(ProjectId projectId, UserId owner, CancellationToken cancellationToken) =>
        // Owner-scoped set-null of tasks.project_id (the move_to_inbox task disposition, FR-014/EC-03). Same
        // table as TaskRepository (shared AppDbContext); ExecuteUpdate writes the raw nullable Guid.
        db.Tasks
            .Where(t => t.ProjectId == projectId && t.CreatedBy == owner && t.DeletedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.ProjectId, (ProjectId?)null), cancellationToken);

    public void Add(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        db.Projects.Add(project);
    }

    public void Remove(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        db.Projects.Remove(project);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // The `version` column is an EF concurrency token (ProjectConfiguration), so an interleaved
            // write that changed it between this handler's version-compare and its commit produces a
            // 0-rows-affected UPDATE → DbUpdateConcurrencyException. Translate that EF-specific signal
            // into the Application-layer VersionConflictException (→ 409) at the persistence seam, so the
            // edit/archive/unarchive/delete handlers stay free of any EF dependency (clean-architecture
            // dependency direction; mirrors TaskRepository). This is the interleaved-race backstop the
            // in-handler version compare cannot close on its own (R12, data-model §2).
            throw new VersionConflictException(
                "The project was modified by another request; reload and retry.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The only unique constraint on `projects` is the PK on `id` — so a 23505 here is a
            // concurrent double-insert of the same client-generated id. DETACH the rejected entity first:
            // it is still tracked in the `Added` state, which would (1) make the handler's re-resolve
            // return this optimistic shadow via EF identity resolution instead of the persisted row, and
            // (2) cause Wolverine's per-message transaction commit (AutoApplyTransactions) to re-attempt
            // the INSERT → an uncaught 500. (Mirrors TaskRepository; the CreateProject handler, T013,
            // re-resolves the race through the same find-then-decide path.)
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            throw new DuplicateProjectIdException("A project with this id already exists.", ex);
        }
    }
}
