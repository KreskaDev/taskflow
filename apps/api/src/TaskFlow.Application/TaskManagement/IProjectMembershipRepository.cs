using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Persistence seam for the <see cref="ProjectMembership"/> entity (ENT-07, data-model.md §6). Defined in
/// the Application layer (implemented in Infrastructure over EF Core) so handlers never depend on the
/// persistence technology directly. The membership set is loaded and mutated <b>transactionally alongside
/// the owning <see cref="Project"/> aggregate</b> (one-aggregate-per-transaction, ADR-0003 — R1/R15);
/// rows carry no concurrency token of their own (the Project <c>version</c> guards them, R11).
/// </summary>
public interface IProjectMembershipRepository
{
    /// <summary>Lists all membership rows for a project (the roster + the per-request authorization set).</summary>
    Task<IReadOnlyList<ProjectMembership>> ListByProjectAsync(ProjectId projectId, CancellationToken cancellationToken);

    /// <summary>Finds the single <c>(projectId, userId)</c> membership row, or <c>null</c> if none.</summary>
    Task<ProjectMembership?> FindAsync(ProjectId projectId, UserId userId, CancellationToken cancellationToken);

    /// <summary>Lists the project ids of the SHARED projects <paramref name="userId"/> is a member of (the sidebar's shared-projects set).</summary>
    Task<IReadOnlyList<ProjectId>> ListProjectIdsForUserAsync(UserId userId, CancellationToken cancellationToken);

    /// <summary>Stages a new membership row for insertion.</summary>
    void Add(ProjectMembership membership);

    /// <summary>Stages a membership row for deletion.</summary>
    void Remove(ProjectMembership membership);

    /// <summary>Removes ALL membership rows of a project (the unshare revoke-all, R3/R10). Returns the count removed.</summary>
    Task<int> RemoveAllForProjectAsync(ProjectId projectId, CancellationToken cancellationToken);

    /// <summary>Commits staged changes to the database.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
