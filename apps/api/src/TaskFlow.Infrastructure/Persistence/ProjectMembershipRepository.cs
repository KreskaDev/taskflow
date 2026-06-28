using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaskFlow.Application.Errors;
using TaskFlow.Application.TaskManagement;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IProjectMembershipRepository"/> over <see cref="AppDbContext"/>.
/// The context is the Wolverine-integrated scoped DbContext, so writes participate in the per-message
/// transaction/outbox alongside the owning <see cref="Project"/> aggregate's mutation. Mirrors
/// <see cref="ProjectRepository"/>'s <see cref="DbUpdateConcurrencyException"/> →
/// <see cref="VersionConflictException"/> translation (the Project <c>version</c> token guards the whole
/// sharing state, R11) and adds the UNIQUE <c>(project_id, user_id)</c> violation translation.
/// </summary>
public sealed class ProjectMembershipRepository(AppDbContext db) : IProjectMembershipRepository
{
    private const string UniqueIndexName = "ux_project_memberships_project_user";

    public async Task<IReadOnlyList<ProjectMembership>> ListByProjectAsync(ProjectId projectId, CancellationToken cancellationToken) =>
        await db.ProjectMemberships
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<ProjectMembership?> FindAsync(ProjectId projectId, UserId userId, CancellationToken cancellationToken) =>
        db.ProjectMemberships.FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<ProjectId>> ListProjectIdsForUserAsync(UserId userId, CancellationToken cancellationToken) =>
        await db.ProjectMemberships
            .Where(m => m.UserId == userId)
            .Select(m => m.ProjectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(ProjectMembership membership)
    {
        ArgumentNullException.ThrowIfNull(membership);
        db.ProjectMemberships.Add(membership);
    }

    public void Remove(ProjectMembership membership)
    {
        ArgumentNullException.ThrowIfNull(membership);
        db.ProjectMemberships.Remove(membership);
    }

    public Task<int> RemoveAllForProjectAsync(ProjectId projectId, CancellationToken cancellationToken) =>
        // Bulk revoke-all on unshare (R3/R10). ExecuteDelete runs immediately within the per-message
        // transaction (mirrors ProjectRepository's ExecuteUpdate dispositions) — the Project's visibility
        // flip is committed by the same handler's SaveChanges, atomic via the Wolverine transaction.
        db.ProjectMemberships
            .Where(m => m.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // The owning Project's `version` concurrency token tripped (an interleaved sharing-state write
            // changed it between the handler's version-compare and commit). Translate to the Application
            // VersionConflictException (→ 409) at the seam, keeping handlers EF-free (mirrors ProjectRepository, R11).
            throw new VersionConflictException(
                "The project's sharing state was modified by another request; reload and retry.", ex);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            && pg.ConstraintName == UniqueIndexName)
        {
            // A concurrent double-invite of the same (project_id, user_id) raced past the handler's
            // FindAsync pre-check. DETACH the rejected entity (it is still tracked Added — leaving it would
            // make a re-resolve return the optimistic shadow and re-attempt the INSERT on commit), then
            // surface the internal duplicate signal the InviteMember handler maps to its 422 (R4).
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            throw new DuplicateMembershipException("This user is already a member of the project.", ex);
        }
    }
}
