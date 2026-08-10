using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Time;
using DomainTaskStatus = TaskFlow.Domain.TaskManagement.TaskStatus;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;

namespace TaskFlow.Application.TaskManagement.Queries;

/// <summary>
/// The sidebar view counts (slice 019, FR-109; contracts/view-counts.md): the number of
/// INCOMPLETE (status ∉ {done, cancelled}) tasks each primary view would list, plus one
/// entry per accessible non-archived project. Carries no wire fields — the caller is
/// resolved from <see cref="ICurrentUser"/>.
/// </summary>
public sealed record GetViewCounts;

/// <summary>One per-project count entry of <see cref="ViewCountsResponse"/>.</summary>
public sealed record ProjectCountResponse
{
    /// <summary>The project identifier (client joins by id; order unspecified).</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>Incomplete tasks in that project.</summary>
    public required int Count { get; init; }
}

/// <summary>The <c>GET /api/views/counts</c> read model (contracts/view-counts.md).</summary>
public sealed record ViewCountsResponse
{
    /// <summary>Incomplete unprojected tasks of the caller (FR-021 scope).</summary>
    public required int Inbox { get; init; }

    /// <summary>Incomplete tasks due today in Warsaw PLUS overdue (FR-022 scope + overdue clause).</summary>
    public required int Today { get; init; }

    /// <summary>Incomplete tasks due within the Upcoming window (next 7 Warsaw days, FR-023 scope).</summary>
    public required int Upcoming { get; init; }

    /// <summary>Incomplete tasks assigned to the caller across accessible projects.</summary>
    public required int Assigned { get; init; }

    /// <summary>One entry per accessible, NON-archived project (membership or ownership).</summary>
    public required IReadOnlyList<ProjectCountResponse> Projects { get; init; }
}

/// <summary>
/// Handles <see cref="GetViewCounts"/>. Authentication is enforced upstream by the deny-by-default
/// middleware; every slice reuses the EXACT repository reads its corresponding view query uses, so a
/// count can never drift from the listing it summarizes (the data-model.md invariant "a count MUST
/// equal the length of the corresponding view listing filtered to incomplete" holds by construction).
/// </summary>
/// <remarks>
/// Scoping (FR-065/FR-068): inbox = owner-scoped unprojected; today/upcoming = the dispatch-by-visibility
/// read arm of the daily views (own + current-membership shared, Europe/Warsaw boundaries via
/// <see cref="WarsawDayBounds"/>); assigned = the GetAssignedToMe working set; projects = the
/// GetMyProjects listing (owned ∪ member-of, archived excluded), each counted with the same
/// project-task listing the project view uses.
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors GetTodayTasksHandler).")]
public static class GetViewCountsHandler
{
    private static bool IsIncomplete(TaskEntity task) =>
        task.Status is not (DomainTaskStatus.Done or DomainTaskStatus.Cancelled);

    public static async Task<ViewCountsResponse> Handle(
        GetViewCounts query,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var caller = currentUser.Id;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var startOfTomorrowUtc = WarsawDayBounds.StartOfTomorrowUtc(now);
        var startOfDayPlus8Utc = WarsawDayBounds.StartOfDayPlusUtc(now, 8);

        // Inbox (FR-021): the caller's unprojected, non-deleted tasks — filtered to incomplete.
        var inboxRows = await tasks.ListOwnedAsync(caller, cancellationToken).ConfigureAwait(false);
        var inbox = inboxRows.Count(IsIncomplete);

        // Today + Upcoming: the same reads GetTodayTasks/GetUpcomingTasks use (already
        // incomplete-only at the repository level; the filter is kept for explicitness).
        var memberOf = await members.ListProjectIdsForUserAsync(caller, cancellationToken).ConfigureAwait(false);
        var todayRows = await tasks
            .ListDueInRangeReadableAsync(caller, memberOf, lowerInclusiveUtc: null, upperExclusiveUtc: startOfTomorrowUtc, cancellationToken)
            .ConfigureAwait(false);
        var upcomingRows = await tasks
            .ListDueInRangeReadableAsync(caller, memberOf, lowerInclusiveUtc: startOfTomorrowUtc, upperExclusiveUtc: startOfDayPlus8Utc, cancellationToken)
            .ConfigureAwait(false);

        // Assigned (FR-071 scope): the GetAssignedToMe working set — assigned to the caller,
        // inside a project the caller can still access (membership or owned-shared).
        var ownedShared = await projects.ListOwnedSharedProjectIdsAsync(caller, cancellationToken).ConfigureAwait(false);
        var readable = memberOf.Concat(ownedShared).ToHashSet();
        var assignedRows = await tasks.ListAssignedToAsync(caller, cancellationToken).ConfigureAwait(false);
        var assigned = assignedRows.Count(t => IsIncomplete(t) && t.ProjectId is { } pid && readable.Contains(pid));

        // Projects: the GetMyProjects listing (owned ∪ member-of, archived excluded), counted with
        // the project view's own listing (scoped by the PROJECT owner, as GetProjectTasks does).
        var owned = await projects.ListOwnedAsync(caller, includeArchived: false, cancellationToken).ConfigureAwait(false);
        var memberProjects = await projects.ListByIdsAsync(memberOf, includeArchived: false, cancellationToken).ConfigureAwait(false);
        var accessible = owned.Concat(memberProjects)
            .GroupBy(p => p.Id)
            .Select(g => g.First())
            .ToList();

        var projectCounts = new List<ProjectCountResponse>(accessible.Count);
        foreach (var project in accessible)
        {
            var projectRows = await tasks
                .ListByProjectAsync(project.Id, project.OwnerId, cancellationToken)
                .ConfigureAwait(false);
            projectCounts.Add(new ProjectCountResponse
            {
                ProjectId = project.Id.Value,
                Count = projectRows.Count(IsIncomplete),
            });
        }

        return new ViewCountsResponse
        {
            Inbox = inbox,
            Today = todayRows.Count(IsIncomplete),
            Upcoming = upcomingRows.Count(IsIncomplete),
            Assigned = assigned,
            Projects = projectCounts,
        };
    }
}
