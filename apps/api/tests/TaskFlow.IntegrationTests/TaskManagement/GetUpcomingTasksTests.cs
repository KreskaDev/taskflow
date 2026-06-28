using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.IntegrationTests.Infrastructure;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T016/T043, US1) for <c>GET /api/tasks/upcoming</c> (operationId
/// <c>getUpcomingTasks</c>, slice 005, US-08.AS-02). Clock frozen to 2026-06-27 12:00Z. Covers the 7-day
/// window <c>[start of tomorrow-Warsaw, start of (today+8)-Warsaw)</c> (day-8 excluded), the
/// tomorrow-in-Upcoming-NOT-Today partition (Constitution X), grouping by the Warsaw <c>LocalDate</c> (NOT
/// the truncated UTC date), groups ascending by date, and the shared-project read arm (member sees it;
/// non-member does not).
/// </summary>
public sealed class GetUpcomingTasksTests : SharingTestBase
{
    private const string UpcomingPath = "/api/tasks/upcoming";

    // Frozen "now": 2026-06-27 12:00Z. Window = [2026-06-27T22:00Z, 2026-07-04T22:00Z) — Jun 28 .. Jul 4.
    private static readonly DateTimeOffset FrozenNow = new(2026, 6, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Today = new(2026, 6, 27, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Tomorrow = new(2026, 6, 28, 10, 0, 0, DateTimeKind.Utc);   // Warsaw 2026-06-28
    private static readonly DateTime Day3 = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);       // Warsaw 2026-06-30
    private static readonly DateTime Day7 = new(2026, 7, 4, 20, 0, 0, DateTimeKind.Utc);        // Warsaw 2026-07-04 22:00 (in window)
    private static readonly DateTime Day8 = new(2026, 7, 5, 10, 0, 0, DateTimeKind.Utc);        // Warsaw 2026-07-05 (excluded)
    private static readonly DateTime SeamNextWarsawDay = new(2026, 6, 28, 22, 30, 0, DateTimeKind.Utc); // 00:30 Warsaw → 2026-06-29

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(FrozenNow));
    }

    [Fact]
    public async Task Allow_returns_the_next_7_days_grouped_by_day_ascending_excluding_today_and_no_due()
    {
        var owner = await CreateUserAsync("g-up-a", "upa@example.com", "Owner");
        var tomorrowId = await SeedTaskAsync(owner, "Tomorrow", "a0", dueDate: Tomorrow, dueHasTime: true);
        var day3Id = await SeedTaskAsync(owner, "Day 3", "a1", dueDate: Day3, dueHasTime: true);
        await SeedTaskAsync(owner, "Today (excluded)", "a2", dueDate: Today, dueHasTime: true);
        await SeedTaskAsync(owner, "No due (excluded)", "a3");
        await SeedTaskAsync(owner, "Done (excluded)", "a4", dueDate: Tomorrow, dueHasTime: true, done: true);

        using var response = await SendAsync(HttpMethod.Get, UpcomingPath, TokenFor(owner));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var groups = (await response.ReadUpcomingAsync()).Groups;
        groups.Select(g => g.Date).Should().Equal("2026-06-28", "2026-06-30").And.BeInAscendingOrder();
        groups.SelectMany(g => g.Tasks).Select(t => t.Id).Should().BeEquivalentTo([tomorrowId, day3Id],
            "a task due tomorrow appears in Upcoming (not Today); today/no-due/done are excluded");
    }

    [Fact]
    public async Task Allow_group_key_is_the_Warsaw_local_date_not_the_truncated_UTC_date()
    {
        var owner = await CreateUserAsync("g-up-seam", "upseam@example.com", "Owner");
        var seamId = await SeedTaskAsync(owner, "Late Warsaw evening", "a0", dueDate: SeamNextWarsawDay, dueHasTime: true);

        using var response = await SendAsync(HttpMethod.Get, UpcomingPath, TokenFor(owner));

        var groups = (await response.ReadUpcomingAsync()).Groups;
        groups.Should().ContainSingle(g => g.Date == "2026-06-29", "22:30Z is 00:30 Warsaw the 29th — grouped by the Warsaw date, not the UTC date 2026-06-28");
        groups.Single().Tasks.Single().Id.Should().Be(seamId);
    }

    [Fact]
    public async Task Allow_day7_is_in_the_window_and_day8_is_excluded()
    {
        var owner = await CreateUserAsync("g-up-edge", "upedge@example.com", "Owner");
        var day7Id = await SeedTaskAsync(owner, "Day 7", "a0", dueDate: Day7, dueHasTime: true);
        await SeedTaskAsync(owner, "Day 8", "a1", dueDate: Day8, dueHasTime: true);

        using var response = await SendAsync(HttpMethod.Get, UpcomingPath, TokenFor(owner));

        var ids = (await response.ReadUpcomingAsync()).Groups.SelectMany(g => g.Tasks).Select(t => t.Id).ToList();
        ids.Should().Contain(day7Id, "the 7th day (Warsaw 2026-07-04) is inside the half-open window");
        ids.Should().HaveCount(1, "the 8th day (Warsaw 2026-07-05) is excluded by the half-open upper bound");
    }

    [Fact]
    public async Task Allow_a_member_sees_a_shared_projects_upcoming_task()
    {
        var owner = await CreateUserAsync("g-up-so", "upso@example.com", "Owner");
        var viewer = await CreateUserAsync("g-up-vw", "upvw@example.com", "Viewer");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);
        var sharedId = await SeedTaskAsync(owner, "Shared upcoming", "a0", projectId: project.Id, dueDate: Tomorrow, dueHasTime: true);

        using var response = await SendAsync(HttpMethod.Get, UpcomingPath, TokenFor(viewer));

        (await response.ReadUpcomingAsync()).Groups.SelectMany(g => g.Tasks).Select(t => t.Id)
            .Should().Contain(sharedId, "a viewer member sees the shared project's upcoming task (R10)");
    }

    [Fact]
    public async Task Deny_a_non_member_does_not_see_a_shared_projects_upcoming_task()
    {
        var owner = await CreateUserAsync("g-up-no", "upno@example.com", "Owner");
        var stranger = await CreateUserAsync("g-up-nx", "upnx@example.com", "Stranger");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        var sharedId = await SeedTaskAsync(owner, "Shared upcoming", "a0", projectId: project.Id, dueDate: Tomorrow, dueHasTime: true);

        using var response = await SendAsync(HttpMethod.Get, UpcomingPath, TokenFor(stranger));

        (await response.ReadUpcomingAsync()).Groups.SelectMany(g => g.Tasks).Select(t => t.Id)
            .Should().NotContain(sharedId, "a non-member never sees another project's task (FR-066)");
    }
}
