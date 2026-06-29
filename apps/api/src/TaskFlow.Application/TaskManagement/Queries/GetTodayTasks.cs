using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Time;

namespace TaskFlow.Application.TaskManagement.Queries;

/// <summary>
/// The Today view (slice 005, AS-01/AS-02, contracts/openapi.yaml <c>getTodayTasks</c>, research R5/R6):
/// the caller's READABLE tasks due today-in-Warsaw OR overdue-incomplete, grouped by project, R5-ordered.
/// Carries no wire fields — the caller is resolved from <see cref="ICurrentUser"/> (R10), never the wire.
/// </summary>
public sealed record GetTodayTasks;

/// <summary>
/// Handles <see cref="GetTodayTasks"/>. Authentication is enforced upstream by the deny-by-default
/// middleware; this handler owns the dispatch-by-visibility read scope + the zone-aware membership +
/// grouping + R5 order.
/// </summary>
/// <remarks>
/// Dispatch-by-visibility read arm (R6/R10): the working set is the caller's own tasks PLUS tasks in the
/// shared projects the caller is a current member of (<see cref="IProjectMembershipRepository.ListProjectIdsForUserAsync"/>);
/// a non-member never sees another project's task (the SC-016 non-member-read-deny = absence). The Warsaw
/// day boundary is computed SERVER-SIDE via <see cref="WarsawDayBounds"/> (the boundary fact is
/// server-authoritative); the SQL filters a plain UTC <c>due_date</c> range (&lt; start-of-tomorrow-Warsaw,
/// no lower bound so overdue is included). Each row's <c>isOverdue</c> = <c>due_date &lt; start-of-today</c>.
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-002 GetMyTasksHandler).")]
public static class GetTodayTasksHandler
{
    public static async Task<TodayResponse> Handle(
        GetTodayTasks query,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        IProjectMembershipRepository members,
        Labels.ITaskLabelRepository taskLabels,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(taskLabels);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var startOfTodayUtc = WarsawDayBounds.StartOfTodayUtc(now);
        var startOfTomorrowUtc = WarsawDayBounds.StartOfTomorrowUtc(now);

        var sharedProjectIds = await members
            .ListProjectIdsForUserAsync(currentUser.Id, cancellationToken)
            .ConfigureAwait(false);

        var rows = await tasks
            .ListDueInRangeReadableAsync(currentUser.Id, sharedProjectIds, lowerInclusiveUtc: null, upperExclusiveUtc: startOfTomorrowUtc, cancellationToken)
            .ConfigureAwait(false);

        // Caller-scoped labels (slice 006, R6): ONE batched join; threaded into the flattened TodayTaskResponse.
        var labelsByTask = await taskLabels
            .ListLabelIdsForTasksAsync(rows.Select(t => t.Id).ToList(), currentUser.Id, cancellationToken)
            .ConfigureAwait(false);

        var groups = rows
            .GroupBy(t => t.ProjectId?.Value)
            .OrderBy(g => g.Key.HasValue) // null (Inbox) group first, then projects
            // Order project groups by the id's ORDINAL STRING form (not Guid.CompareTo) so the order is
            // identical to the client's optimistic recompute (dailyViews.ts) — FR-092, no reconcile flicker.
            .ThenBy(g => g.Key?.ToString(), StringComparer.Ordinal)
            .Select(g => new TodayGroup
            {
                ProjectId = g.Key,
                Tasks = g
                    .OrderBy(t => TaskTriageOrder.PriorityRank(t.Priority))
                    .ThenBy(t => t.DueDate)
                    .ThenBy(t => t.CreatedAt)
                    .ThenBy(t => t.Id.Value.ToString(), StringComparer.Ordinal)
                    .Select(t => TodayTaskResponse.From(
                        t,
                        isOverdue: t.DueDate < startOfTodayUtc,
                        callerLabelIds: labelsByTask.TryGetValue(t.Id, out var ids) ? ids : []))
                    .ToList(),
            })
            .ToList();

        return new TodayResponse { Groups = groups };
    }
}
