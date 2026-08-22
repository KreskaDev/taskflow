using TaskFlow.Application.Authorization;
using TaskFlow.Domain.IdentityAccess;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;

namespace TaskFlow.Application.TaskManagement.Cycles;

/// <summary>
/// The FR-065 visibility dispatch for cycle task ROWS (D10), shared by <c>GetCycleTasks</c> and
/// the <c>CloseCycle</c> override guard: an unprojected task is visible to its owner only; a
/// projected task is visible iff the caller can READ the project — they OWN it (personal or
/// shared, ARCHIVED included per EC-12) or hold a current membership. <c>createdBy</c> on a
/// projected task confers NO standalone access (FR-066 — a former member's authorship is
/// provenance only).
/// </summary>
internal static class CycleTaskVisibility
{
    /// <summary>The caller's readable project id set: owned (active ∪ archived) ∪ current memberships.</summary>
    public static async System.Threading.Tasks.Task<HashSet<ProjectId>> ListReadableProjectIdsAsync(
        ICurrentUser caller,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        CancellationToken cancellationToken)
    {
        var memberOf = await members.ListProjectIdsForUserAsync(caller.Id, cancellationToken).ConfigureAwait(false);
        var ownedActive = await projects.ListOwnedAsync(caller.Id, includeArchived: false, cancellationToken).ConfigureAwait(false);
        var ownedArchived = await projects.ListOwnedAsync(caller.Id, includeArchived: true, cancellationToken).ConfigureAwait(false);

        return memberOf
            .Concat(ownedActive.Select(p => p.Id))
            .Concat(ownedArchived.Select(p => p.Id))
            .ToHashSet();
    }

    /// <summary>Whether <paramref name="task"/> is visible to <paramref name="caller"/> under the dispatch.</summary>
    public static bool IsVisible(TaskEntity task, UserId caller, IReadOnlySet<ProjectId> readableProjects) =>
        task.ProjectId is { } pid
            ? readableProjects.Contains(pid)
            : task.CreatedBy == caller;
}
