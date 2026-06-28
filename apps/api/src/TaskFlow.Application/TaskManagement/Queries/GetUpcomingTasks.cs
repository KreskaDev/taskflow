using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Time;

namespace TaskFlow.Application.TaskManagement.Queries;

/// <summary>
/// The Upcoming view (slice 005, US-08.AS-02, contracts/openapi.yaml <c>getUpcomingTasks</c>, research
/// R5/R6): the caller's READABLE tasks due in the next 7 Warsaw days, grouped by Warsaw calendar day,
/// R5-ordered within each day, groups ascending by date. Carries no wire fields — the caller is resolved
/// from <see cref="ICurrentUser"/> (R10).
/// </summary>
public sealed record GetUpcomingTasks;

/// <summary>
/// Handles <see cref="GetUpcomingTasks"/>. Authentication is enforced upstream by the deny-by-default
/// middleware; this handler owns the dispatch-by-visibility read scope + the zone-aware window + grouping.
/// </summary>
/// <remarks>
/// Same dispatch-by-visibility read arm as <see cref="GetTodayTasksHandler"/>. The window is
/// <c>[start of tomorrow-Warsaw, start of (today+8)-Warsaw)</c> (the 7 calendar days after today —
/// Constitution X partitions Today/Upcoming, no overlap). The group key is each task's <b>Warsaw</b>
/// <c>LocalDate</c> (<see cref="WarsawDayBounds.WarsawLocalDate"/>), NOT the truncated UTC date (R1/R3).
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-002 GetMyTasksHandler).")]
public static class GetUpcomingTasksHandler
{
    public static async Task<UpcomingResponse> Handle(
        GetUpcomingTasks query,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        IProjectMembershipRepository members,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var startOfTomorrowUtc = WarsawDayBounds.StartOfTomorrowUtc(now);
        var startOfDayPlus8Utc = WarsawDayBounds.StartOfDayPlusUtc(now, 8);

        var sharedProjectIds = await members
            .ListProjectIdsForUserAsync(currentUser.Id, cancellationToken)
            .ConfigureAwait(false);

        var rows = await tasks
            .ListDueInRangeReadableAsync(currentUser.Id, sharedProjectIds, lowerInclusiveUtc: startOfTomorrowUtc, upperExclusiveUtc: startOfDayPlus8Utc, cancellationToken)
            .ConfigureAwait(false);

        var groups = rows
            .GroupBy(t => WarsawDayBounds.WarsawLocalDate(t.DueDate!.Value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new UpcomingGroup
            {
                Date = g.Key,
                Tasks = g
                    .OrderBy(t => TaskTriageOrder.PriorityRank(t.Priority))
                    .ThenBy(t => t.DueDate)
                    .ThenBy(t => t.CreatedAt)
                    .ThenBy(t => t.Id.Value)
                    .Select(TaskResponse.From)
                    .ToList(),
            })
            .ToList();

        return new UpcomingResponse { Groups = groups };
    }
}
