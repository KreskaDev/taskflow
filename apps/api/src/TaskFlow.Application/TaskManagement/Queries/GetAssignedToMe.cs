using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;

namespace TaskFlow.Application.TaskManagement.Queries;

/// <summary>
/// The "Assigned to me" view (slice 008, AS-03, FR-071, contracts/openapi.yaml <c>getAssignedToMe</c>,
/// research R6): the caller's tasks across SHARED projects where the caller is BOTH a current member (or the
/// owner) AND an assignee. Carries no wire fields — the caller is resolved from <see cref="ICurrentUser"/>.
/// </summary>
public sealed record GetAssignedToMe;

/// <summary>
/// Handles <see cref="GetAssignedToMe"/>. Authentication is enforced upstream; this handler owns the
/// membership-dispatched scoping (assignee is provenance only — FR-066).
/// </summary>
/// <remarks>
/// Readable shared set = <c>ListProjectIdsForUserAsync(caller)</c> ∪ <c>ListOwnedSharedProjectIdsAsync(caller)</c>
/// (the owner has no membership row, R6). The caller's assigned active tasks are loaded, then filtered to that
/// set in-memory (a small set; avoids an IN over the value-converted nullable project FK — the slice-005
/// lesson), grouped by project (ordinal id order), R5-ordered within each group. A user who lost membership
/// sees nothing (membership gates; the assignee row is provenance only).
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-005 GetTodayTasksHandler).")]
public static class GetAssignedToMeHandler
{
    public static async Task<AssignedResponse> Handle(
        GetAssignedToMe query,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        IProjectMembershipRepository members,
        IProjectRepository projects,
        Labels.ITaskLabelRepository taskLabels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(taskLabels);

        var memberOf = await members.ListProjectIdsForUserAsync(currentUser.Id, cancellationToken).ConfigureAwait(false);
        var ownedShared = await projects.ListOwnedSharedProjectIdsAsync(currentUser.Id, cancellationToken).ConfigureAwait(false);
        var readable = memberOf.Concat(ownedShared).ToHashSet();

        var assigned = await tasks.ListAssignedToAsync(currentUser.Id, cancellationToken).ConfigureAwait(false);

        // Caller-scoped labels (slice 006, R6): ONE batched join over the assigned rows.
        var labelsByTask = await taskLabels
            .ListLabelIdsForTasksAsync(assigned.Select(t => t.Id).ToList(), currentUser.Id, cancellationToken)
            .ConfigureAwait(false);

        var groups = assigned
            .Where(t => t.ProjectId is { } pid && readable.Contains(pid))
            .GroupBy(t => t.ProjectId!.Value.Value) // the shared project's Guid
            .OrderBy(g => g.Key.ToString(), StringComparer.Ordinal)
            .Select(g => new AssignedGroup
            {
                ProjectId = g.Key,
                Tasks = g
                    .OrderBy(t => TaskTriageOrder.PriorityRank(t.Priority))
                    .ThenBy(t => t.DueDate)
                    .ThenBy(t => t.CreatedAt)
                    .ThenBy(t => t.Id.Value.ToString(), StringComparer.Ordinal)
                    .Select(t => TaskResponse.From(t, labelsByTask.TryGetValue(t.Id, out var ids) ? ids : []))
                    .ToList(),
            })
            .ToList();

        return new AssignedResponse { Groups = groups };
    }
}
