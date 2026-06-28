using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.IntegrationTests.Infrastructure;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T014/T043, US1) for <c>GET /api/tasks/today</c> (operationId <c>getTodayTasks</c>,
/// slice 005, AS-01/AS-02). The clock is frozen to the quickstart reference (2026-06-27 12:00Z = 14:00 Warsaw,
/// CEST) so the Warsaw day boundary is deterministic. Covers due-today + overdue (flagged), the
/// done/cancelled/no-due exclusions, the project grouping + R5 order (NULL priority last), the same-Warsaw-day
/// seam, and the dispatch-by-visibility read arm: a member sees a shared project's task; a non-member does not.
/// </summary>
public sealed class GetTodayTasksTests : SharingTestBase
{
    private const string TodayPath = "/api/tasks/today";

    // Frozen "now": 2026-06-27 12:00Z. StartOfToday-Warsaw = 2026-06-26T22:00Z; StartOfTomorrow = 2026-06-27T22:00Z.
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
    public async Task Allow_includes_due_today_and_overdue_and_excludes_done_cancelled_and_no_due()
    {
        var owner = await CreateUserAsync("g-td-a", "tda@example.com", "Owner");
        var todayId = await SeedTaskAsync(owner, "Due today", "a1", dueDate: DueToday, dueHasTime: true, priority: "P1");
        var overdueId = await SeedTaskAsync(owner, "Overdue", "a0", dueDate: Overdue, dueHasTime: true, priority: "P0");
        await SeedTaskAsync(owner, "Tomorrow", "a2", dueDate: Tomorrow, dueHasTime: true);
        await SeedTaskAsync(owner, "No due", "a3");
        await SeedTaskAsync(owner, "Done today", "a4", dueDate: DueToday, dueHasTime: true, done: true);

        using var response = await SendAsync(HttpMethod.Get, TodayPath, TokenFor(owner));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var all = (await response.ReadTodayAsync()).Groups.SelectMany(g => g.Tasks).ToList();
        all.Select(t => t.Id).Should().BeEquivalentTo([todayId, overdueId], "only due-today + overdue-incomplete appear");
        all.Single(t => t.Id == overdueId).IsOverdue.Should().BeTrue();
        all.Single(t => t.Id == todayId).IsOverdue.Should().BeFalse("a due-today task is not overdue");
    }

    [Fact]
    public async Task Allow_groups_by_project_inbox_first_and_orders_null_priority_last()
    {
        var owner = await CreateUserAsync("g-td-grp", "tdgrp@example.com", "Owner");
        var token = TokenFor(owner);
        var project = await CreateProjectAsync(token, name: "Work");

        var inboxHigh = await SeedTaskAsync(owner, "Inbox P2", "a0", dueDate: DueToday, dueHasTime: true, priority: "P2");
        var inboxNone = await SeedTaskAsync(owner, "Inbox none", "a1", dueDate: DueToday, dueHasTime: true);
        var projTask = await SeedTaskAsync(owner, "Project P0", "a2", projectId: project.Id, dueDate: DueToday, dueHasTime: true, priority: "P0");

        using var response = await SendAsync(HttpMethod.Get, TodayPath, token);

        var groups = (await response.ReadTodayAsync()).Groups;
        groups.Should().HaveCount(2);
        groups[0].ProjectId.Should().BeNull("the Inbox/unprojected group sorts first");
        groups[0].Tasks.Select(t => t.Id).Should().Equal(new[] { inboxHigh, inboxNone }, "P2 before NULL-priority (null sorts last)");
        groups[1].ProjectId.Should().Be(project.Id);
        groups[1].Tasks.Single().Id.Should().Be(projTask);
    }

    [Fact]
    public async Task Allow_membership_is_by_the_Warsaw_calendar_day_at_the_UTC_midnight_seam()
    {
        var owner = await CreateUserAsync("g-td-seam", "tdseam@example.com", "Owner");
        var inToday = await SeedTaskAsync(owner, "23:30 Warsaw today", "a0", dueDate: SeamInToday, dueHasTime: true);
        var nextDay = await SeedTaskAsync(owner, "00:30 Warsaw tomorrow", "a1", dueDate: SeamNextDay, dueHasTime: true);

        using var response = await SendAsync(HttpMethod.Get, TodayPath, TokenFor(owner));

        var ids = (await response.ReadTodayAsync()).Groups.SelectMany(g => g.Tasks).Select(t => t.Id).ToList();
        ids.Should().Contain(inToday, "21:30Z is 23:30 Warsaw — still the 27th, in Today");
        ids.Should().NotContain(nextDay, "22:30Z is 00:30 Warsaw — the 28th, NOT in Today (it is Upcoming)");
    }

    [Fact]
    public async Task Allow_a_member_sees_a_shared_projects_task_in_today()
    {
        var owner = await CreateUserAsync("g-td-so", "tdso@example.com", "Owner");
        var viewer = await CreateUserAsync("g-td-vw", "tdvw@example.com", "Viewer");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);
        var sharedId = await SeedTaskAsync(owner, "Shared due today", "a0", projectId: project.Id, dueDate: DueToday, dueHasTime: true);

        using var response = await SendAsync(HttpMethod.Get, TodayPath, TokenFor(viewer));

        var today = await response.ReadTodayAsync();
        today.Groups.Should().ContainSingle(g => g.ProjectId == project.Id, "the shared project is its own group in the member's Today");
        today.Groups.SelectMany(g => g.Tasks).Select(t => t.Id).Should().Contain(sharedId, "a viewer member sees the shared project's task (R10)");
    }

    [Fact]
    public async Task Deny_a_non_member_does_not_see_a_shared_projects_task_in_today()
    {
        // SC-016 non-member-read-deny (collection read → absence, not a 404 body).
        var owner = await CreateUserAsync("g-td-no", "tdno@example.com", "Owner");
        var stranger = await CreateUserAsync("g-td-nx", "tdnx@example.com", "Stranger");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        var sharedId = await SeedTaskAsync(owner, "Shared due today", "a0", projectId: project.Id, dueDate: DueToday, dueHasTime: true);

        using var response = await SendAsync(HttpMethod.Get, TodayPath, TokenFor(stranger));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadTodayAsync()).Groups.SelectMany(g => g.Tasks).Select(t => t.Id)
            .Should().NotContain(sharedId, "a non-member never sees another project's task (FR-066)");
    }

    [Fact]
    public async Task Deny_another_users_personal_task_is_absent_from_my_today()
    {
        var owner = await CreateUserAsync("g-td-iso-o", "tdisoo@example.com", "Owner");
        var caller = await CreateUserAsync("g-td-iso-c", "tdisoc@example.com", "Caller");
        var foreignId = await SeedTaskAsync(owner, "Owner's due-today", "a0", dueDate: DueToday, dueHasTime: true);

        using var response = await SendAsync(HttpMethod.Get, TodayPath, TokenFor(caller));

        (await response.ReadTodayAsync()).Groups.SelectMany(g => g.Tasks).Select(t => t.Id)
            .Should().NotContain(foreignId, "owner-scoped: a foreign personal task is absent (per-user isolation)");
    }

    [Fact]
    public async Task Deny_no_jwt_is_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(TodayPath, UriKind.Relative));
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the Today read is deny-by-default (FR-068)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
    }
}
