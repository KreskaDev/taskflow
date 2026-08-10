using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.IntegrationTests.Infrastructure;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (slice 019, T029; FR-109, FR-065/FR-068) for <c>GET /api/views/counts</c>
/// (operationId <c>getViewCounts</c>, contracts/view-counts.md). Counts are the number of INCOMPLETE
/// (status ∉ {done, cancelled}) tasks each view would list, computed with the same Europe/Warsaw
/// boundaries as the view queries — asserted here by comparing each count against the corresponding
/// LISTING's incomplete-filtered length, not just a literal.
/// </summary>
public sealed class ViewCountsTests : SharingTestBase
{
    private const string CountsPath = "/api/views/counts";

    // Frozen "now": 2026-06-27 12:00Z (14:00 Warsaw, CEST) — same anchor as GetTodayTasksTests.
    private static readonly DateTimeOffset FrozenNow = new(2026, 6, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime DueToday = new(2026, 6, 27, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Overdue = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Tomorrow = new(2026, 6, 28, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SeamInToday = new(2026, 6, 27, 21, 30, 0, DateTimeKind.Utc); // 23:30 Warsaw, the 27th
    private static readonly DateTime SeamNextDay = new(2026, 6, 27, 22, 30, 0, DateTimeKind.Utc); // 00:30 Warsaw, the 28th

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(FrozenNow));
    }

    [Fact]
    public async Task Allow_counts_equal_the_incomplete_filtered_view_listing_lengths()
    {
        var owner = await CreateUserAsync("g-vc-a", "vca@example.com", "Owner");
        var token = TokenFor(owner);
        var project = await CreateProjectAsync(token, name: "Praca");

        // Inbox: 2 incomplete + 1 done (excluded from the count, still listed by the view).
        await SeedTaskAsync(owner, "Inbox A", "a0");
        await SeedTaskAsync(owner, "Inbox B", "a1", dueDate: Overdue, dueHasTime: true); // also counts in today (overdue)
        await SeedTaskAsync(owner, "Inbox done", "a2", done: true);

        // Today: the overdue one above + one due today; a DONE due-today task is excluded.
        await SeedTaskAsync(owner, "Due today", "a3", dueDate: DueToday, dueHasTime: true);
        await SeedTaskAsync(owner, "Due today done", "a4", dueDate: DueToday, dueHasTime: true, done: true);

        // Upcoming: one tomorrow.
        await SeedTaskAsync(owner, "Tomorrow", "a5", dueDate: Tomorrow, dueHasTime: true);

        // Project: 2 incomplete + 1 done in the project (dateless → not in today/upcoming).
        await SeedTaskAsync(owner, "Proj A", "b0", projectId: project.Id);
        await SeedTaskAsync(owner, "Proj B", "b1", projectId: project.Id);
        await SeedTaskAsync(owner, "Proj done", "b2", projectId: project.Id, done: true);

        using var response = await SendAsync(HttpMethod.Get, CountsPath, token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var counts = await response.ReadCountsAsync();

        // Cross-checked against the actual view listings, filtered to incomplete.
        using var inboxList = await SendAsync(HttpMethod.Get, "/api/tasks", token);
        var inboxIncomplete = (await inboxList.ReadTasksAsync()).Count(t => t.Status is not ("done" or "cancelled"));
        counts.Inbox.Should().Be(inboxIncomplete).And.Be(4, "Inbox A + Inbox B + Due today + Tomorrow (unprojected, incomplete; done excluded)");

        using var todayList = await SendAsync(HttpMethod.Get, "/api/tasks/today", token);
        var todayLen = (await todayList.ReadTodayAsync()).Groups.SelectMany(g => g.Tasks).Count();
        counts.Today.Should().Be(todayLen).And.Be(2, "overdue Inbox B + Due today; done excluded");

        using var upcomingList = await SendAsync(HttpMethod.Get, "/api/tasks/upcoming", token);
        var upcomingLen = (await upcomingList.ReadUpcomingAsync()).Groups.SelectMany(g => g.Tasks).Count();
        counts.Upcoming.Should().Be(upcomingLen).And.Be(1);

        counts.Assigned.Should().Be(0, "nothing is assigned to the caller");
        counts.Projects.Should().ContainSingle(p => p.ProjectId == project.Id).Which.Count.Should().Be(2);
    }

    [Fact]
    public async Task Allow_today_boundary_follows_the_Warsaw_calendar_day_at_the_UTC_seam()
    {
        var owner = await CreateUserAsync("g-vc-seam", "vcseam@example.com", "Owner");
        await SeedTaskAsync(owner, "23:30 Warsaw today", "a0", dueDate: SeamInToday, dueHasTime: true);
        await SeedTaskAsync(owner, "00:30 Warsaw tomorrow", "a1", dueDate: SeamNextDay, dueHasTime: true);

        using var response = await SendAsync(HttpMethod.Get, CountsPath, TokenFor(owner));
        var counts = await response.ReadCountsAsync();

        counts.Today.Should().Be(1, "21:30Z is still the Warsaw 27th; 22:30Z is the 28th");
        counts.Upcoming.Should().Be(1, "the 22:30Z task falls into tomorrow's window");
    }

    [Fact]
    public async Task Allow_assigned_counts_incomplete_tasks_assigned_to_the_caller_in_accessible_projects()
    {
        var owner = await CreateUserAsync("g-vc-asg-o", "vcasgo@example.com", "Owner");
        var member = await CreateUserAsync("g-vc-asg-m", "vcasgm@example.com", "Member");
        var ownerToken = TokenFor(owner);

        var project = await CreateProjectAsync(ownerToken, name: "Shared");
        var shared = await ShareProjectAsync(ownerToken, project);
        await SeedMembershipAsync(project.Id, member, MembershipRoles.Editor);

        var t1 = await SeedTaskAsync(owner, "Assigned incomplete", "a0", projectId: project.Id);
        var t2 = await SeedTaskAsync(owner, "Assigned done", "a1", projectId: project.Id, done: true);
        _ = shared;

        // Assign both to the member via the real command (owner has Editor+ rights).
        foreach (var (id, version) in new[] { (t1, 0), (t2, 1) })
        {
            using var assign = await SendAsync(
                HttpMethod.Patch, $"/api/tasks/{id}/assignees", ownerToken,
                new { assigneeIds = new[] { member.Value }, version });
            assign.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var response = await SendAsync(HttpMethod.Get, CountsPath, TokenFor(member));
        var counts = await response.ReadCountsAsync();
        counts.Assigned.Should().Be(1, "only the incomplete assigned task counts");
        counts.Projects.Should().ContainSingle(p => p.ProjectId == project.Id, "membership grants the project entry");
    }

    [Fact]
    public async Task Deny_another_users_tasks_and_projects_never_leak_into_the_callers_counts()
    {
        var caller = await CreateUserAsync("g-vc-d1", "vcd1@example.com", "Caller");
        var other = await CreateUserAsync("g-vc-d2", "vcd2@example.com", "Other");

        var otherProject = await CreateProjectAsync(TokenFor(other), name: "Foreign");
        await SeedTaskAsync(other, "Foreign inbox", "a0");
        await SeedTaskAsync(other, "Foreign today", "a1", dueDate: DueToday, dueHasTime: true);
        await SeedTaskAsync(other, "Foreign proj", "b0", projectId: otherProject.Id);

        using var response = await SendAsync(HttpMethod.Get, CountsPath, TokenFor(caller));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var counts = await response.ReadCountsAsync();

        counts.Inbox.Should().Be(0);
        counts.Today.Should().Be(0);
        counts.Upcoming.Should().Be(0);
        counts.Assigned.Should().Be(0);
        counts.Projects.Should().BeEmpty("an authenticated caller with zero data gets zeros, not an error");
    }

    [Fact]
    public async Task Deny_a_project_the_caller_was_removed_from_disappears_from_the_projects_counts()
    {
        var owner = await CreateUserAsync("g-vc-rm-o", "vcrmo@example.com", "Owner");
        var member = await CreateUserAsync("g-vc-rm-m", "vcrmm@example.com", "Member");
        var ownerToken = TokenFor(owner);

        var project = await CreateProjectAsync(ownerToken, name: "Ephemeral");
        var shared = await ShareProjectAsync(ownerToken, project);
        await SeedMembershipAsync(project.Id, member, MembershipRoles.Viewer);
        await SeedTaskAsync(owner, "Proj task", "a0", projectId: project.Id);

        using (var before = await SendAsync(HttpMethod.Get, CountsPath, TokenFor(member)))
        {
            (await before.ReadCountsAsync()).Projects.Should().ContainSingle(p => p.ProjectId == project.Id);
        }

        using var remove = await SendAsync(
            HttpMethod.Delete, $"/api/projects/{project.Id}/members/{member.Value}?version={shared.Version}", ownerToken);
        remove.IsSuccessStatusCode.Should().BeTrue("the owner removes the member");

        using var after = await SendAsync(HttpMethod.Get, CountsPath, TokenFor(member));
        (await after.ReadCountsAsync()).Projects.Should().NotContain(p => p.ProjectId == project.Id);
    }

    [Fact]
    public async Task Allow_archived_projects_are_excluded_from_the_projects_counts()
    {
        var owner = await CreateUserAsync("g-vc-arch", "vcarch@example.com", "Owner");
        var token = TokenFor(owner);
        var project = await CreateProjectAsync(token, name: "To archive");
        await SeedTaskAsync(owner, "Task in archived", "a0", projectId: project.Id);

        using var archive = await SendAsync(
            HttpMethod.Patch, $"/api/projects/{project.Id}/archive", token, new { version = project.Version });
        archive.StatusCode.Should().Be(HttpStatusCode.OK);

        using var response = await SendAsync(HttpMethod.Get, CountsPath, token);
        (await response.ReadCountsAsync()).Projects.Should().NotContain(p => p.ProjectId == project.Id);
    }

    [Fact]
    public async Task Deny_unauthenticated_request_is_401()
    {
        using var response = await SendAsync(HttpMethod.Get, CountsPath, "not-a-valid-token");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
    }
}
