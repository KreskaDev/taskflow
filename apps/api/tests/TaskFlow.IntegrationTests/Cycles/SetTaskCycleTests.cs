using System.Net;
using FluentAssertions;
using TaskFlow.IntegrationTests.Infrastructure;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.Cycles;

/// <summary>
/// Allow + deny coverage (slice 011 T004) for <c>PATCH /api/tasks/{id}/cycle</c> (operationId
/// <c>setTaskCycle</c>, contracts/task-cycle.md). Authorization = the TASK's visibility (FR-065
/// dispatch, the <c>SetPriority</c> guard): personal → owner only (foreign → 404); shared →
/// editor/owner (viewer → 403, non-member/former member → 404). The cycle needs no additional
/// authorization (team-wide); ANY status is assignable, incl. closed. Every write clears
/// <c>carried_over</c> (D7) and bumps the version.
/// </summary>
public sealed class SetTaskCycleTests : CyclesTestBase
{
    [Fact]
    public async Task Allow_owner_assigns_a_personal_task_to_a_cycle()
    {
        var owner = await CreateUserAsync("g-tc-own", "tcown@example.com", "Owner");
        var token = TokenFor(owner);
        var cycle = await CreateCycleAsync(token, "Sprint", Day(0), Day(14));
        var id = await SeedCycleTaskAsync(owner, "Do cyklu");

        using var response = await SendAsync(HttpMethod.Patch, TaskCyclePath(id), token, new { cycleId = cycle.Id, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadTaskAsync();
        body.CycleId.Should().Be(cycle.Id, "TaskResponse now carries the assignment (D16)");
        body.CarriedOver.Should().BeFalse();
        body.Version.Should().Be(1, "a mutating write bumps the optimistic-concurrency token");
        (await LoadTaskCycleStateAsync(id)).Should().Be(((Guid?)cycle.Id, false));
    }

    [Theory]
    [InlineData("planned")]
    [InlineData("active")]
    [InlineData("closed")]
    public async Task Allow_assignment_into_a_cycle_of_any_status(string status)
    {
        var owner = await CreateUserAsync($"g-tc-{status}", $"tc{status}@example.com", "Owner");
        var token = TokenFor(owner);
        var cycle = await CreateCycleAsync(token, "Cel", Day(0), Day(14));
        if (status is "active" or "closed")
        {
            cycle = await ActivateCycleAsync(token, cycle);
        }

        if (status is "closed")
        {
            using var close = await SendAsync(HttpMethod.Patch, ClosePath(cycle.Id), token, new { version = cycle.Version });
            close.StatusCode.Should().Be(HttpStatusCode.OK);
            cycle = (await close.ReadCloseCycleAsync()).Cycle;
        }

        var id = await SeedCycleTaskAsync(owner, "Do cyklu");

        using var response = await SendAsync(HttpMethod.Patch, TaskCyclePath(id), token, new { cycleId = cycle.Id, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "ALL statuses are assignable, incl. closed (Clarifications)");
        (await response.ReadTaskAsync()).CycleId.Should().Be(cycle.Id);
    }

    [Fact]
    public async Task Manual_assignment_into_a_closed_cycle_never_sets_carried_over()
    {
        var owner = await CreateUserAsync("g-tc-mancl", "tcmancl@example.com", "Owner");
        var token = TokenFor(owner);
        var active = await ActivateCycleAsync(token, await CreateCycleAsync(token, "Historia", Day(0), Day(14)));
        using var close = await SendAsync(HttpMethod.Patch, ClosePath(active.Id), token, new { version = active.Version });
        close.StatusCode.Should().Be(HttpStatusCode.OK);
        var closed = (await close.ReadCloseCycleAsync()).Cycle;
        var id = await SeedCycleTaskAsync(owner, "Porządkowanie historii");

        using var response = await SendAsync(HttpMethod.Patch, TaskCyclePath(id), token, new { cycleId = closed.Id, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadTaskCycleStateAsync(id)).Should().Be(
            ((Guid?)closed.Id, false), "carried_over is EXCLUSIVELY rollover-written (D7)");
    }

    [Fact]
    public async Task Deny_an_unknown_cycle_is_422()
    {
        var owner = await CreateUserAsync("g-tc-unk", "tcunk@example.com", "Owner");
        var id = await SeedCycleTaskAsync(owner, "Bez celu");

        using var response = await SendAsync(
            HttpMethod.Patch, TaskCyclePath(id), TokenFor(owner), new { cycleId = Guid.CreateVersion7(), version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "cycleId must reference an existing cycle");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
        (await LoadTaskCycleStateAsync(id)).CycleId.Should().BeNull("the rejected assignment never landed");
    }

    [Fact]
    public async Task Null_clears_the_assignment_and_the_carried_over_flag()
    {
        var owner = await CreateUserAsync("g-tc-null", "tcnull@example.com", "Owner");
        var token = TokenFor(owner);
        var cycle = await CreateCycleAsync(token, "Sprint", Day(0), Day(14));
        var id = await SeedCycleTaskAsync(owner, "Przeniesione kiedyś", cycleId: cycle.Id, carriedOver: true);

        using var response = await SendAsync(HttpMethod.Patch, TaskCyclePath(id), token, new { cycleId = (Guid?)null, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadTaskAsync();
        body.CycleId.Should().BeNull("null = back to the cycle backlog (FR-016)");
        body.CarriedOver.Should().BeFalse("every SetTaskCycle write clears the flag (D7)");
        (await LoadTaskCycleStateAsync(id)).Should().Be(((Guid?)null, false));
    }

    [Fact]
    public async Task Reassignment_clears_the_carried_over_flag()
    {
        var owner = await CreateUserAsync("g-tc-recl", "tcrecl@example.com", "Owner");
        var token = TokenFor(owner);
        var oldCycle = await CreateCycleAsync(token, "Stary", Day(0), Day(14));
        var newCycle = await CreateCycleAsync(token, "Nowy", Day(14), Day(28));
        var id = await SeedCycleTaskAsync(owner, "Przeniesione", cycleId: oldCycle.Id, carriedOver: true);

        using var response = await SendAsync(HttpMethod.Patch, TaskCyclePath(id), token, new { cycleId = newCycle.Id, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadTaskCycleStateAsync(id)).Should().Be(((Guid?)newCycle.Id, false));
    }

    [Fact]
    public async Task Deny_another_users_personal_task_is_404()
    {
        var owner = await CreateUserAsync("g-tc-fo", "tcfo@example.com", "Owner");
        var stranger = await CreateUserAsync("g-tc-fx", "tcfx@example.com", "Stranger");
        var cycle = await CreateCycleAsync(TokenFor(stranger), "Sprint", Day(0), Day(14));
        var id = await SeedCycleTaskAsync(owner, "Cudze zadanie");

        using var response = await SendAsync(
            HttpMethod.Patch, TaskCyclePath(id), TokenFor(stranger), new { cycleId = cycle.Id, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a foreign personal task is not_found, never 403");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
        (await LoadTaskCycleStateAsync(id)).CycleId.Should().BeNull();
    }

    [Fact]
    public async Task Allow_an_editor_member_assigns_a_shared_task()
    {
        var owner = await CreateUserAsync("g-tc-so", "tcso@example.com", "Owner");
        var editor = await CreateUserAsync("g-tc-sed", "tcsed@example.com", "Editor");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        var cycle = await CreateCycleAsync(token, "Sprint", Day(0), Day(14));
        var id = await SeedCycleTaskAsync(owner, "Wspólne", projectId: project.Id);

        using var response = await SendAsync(
            HttpMethod.Patch, TaskCyclePath(id), TokenFor(editor), new { cycleId = cycle.Id, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "an editor member may write a shared-project task (FR-067)");
        (await response.ReadTaskAsync()).CycleId.Should().Be(cycle.Id);
    }

    [Fact]
    public async Task Deny_a_viewer_member_is_403()
    {
        var owner = await CreateUserAsync("g-tc-vo", "tcvo@example.com", "Owner");
        var viewer = await CreateUserAsync("g-tc-vw", "tcvw@example.com", "Viewer");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);
        var cycle = await CreateCycleAsync(token, "Sprint", Day(0), Day(14));
        var id = await SeedCycleTaskAsync(owner, "Wspólne", projectId: project.Id);

        using var response = await SendAsync(
            HttpMethod.Patch, TaskCyclePath(id), TokenFor(viewer), new { cycleId = cycle.Id, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a viewer is a member but lacks write role (FR-067)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
        (await LoadTaskCycleStateAsync(id)).CycleId.Should().BeNull("the viewer's write never landed");
    }

    [Fact]
    public async Task Deny_a_non_member_of_a_shared_project_is_404()
    {
        var owner = await CreateUserAsync("g-tc-no", "tcno@example.com", "Owner");
        var stranger = await CreateUserAsync("g-tc-nx", "tcnx@example.com", "Stranger");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        var cycle = await CreateCycleAsync(token, "Sprint", Day(0), Day(14));
        var id = await SeedCycleTaskAsync(owner, "Wspólne", projectId: project.Id);

        using var response = await SendAsync(
            HttpMethod.Patch, TaskCyclePath(id), TokenFor(stranger), new { cycleId = cycle.Id, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a non-member is not told the shared task exists");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_a_former_member_is_404()
    {
        var owner = await CreateUserAsync("g-tc-fmo", "tcfmo@example.com", "Owner");
        var former = await CreateUserAsync("g-tc-fmx", "tcfmx@example.com", "Former");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, former, MembershipRoles.Editor);
        var cycle = await CreateCycleAsync(token, "Sprint", Day(0), Day(14));
        var id = await SeedCycleTaskAsync(owner, "Wspólne", projectId: project.Id);
        await DeleteMembershipRowAsync(project.Id, former);

        using var response = await SendAsync(
            HttpMethod.Patch, TaskCyclePath(id), TokenFor(former), new { cycleId = cycle.Id, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "membership loss revokes ALL access (FR-066)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_a_stale_version_is_409()
    {
        var owner = await CreateUserAsync("g-tc-st", "tcst@example.com", "Owner");
        var token = TokenFor(owner);
        var cycle = await CreateCycleAsync(token, "Sprint", Day(0), Day(14));
        var id = await SeedCycleTaskAsync(owner, "Wyścig");

        using var response = await SendAsync(HttpMethod.Patch, TaskCyclePath(id), token, new { cycleId = cycle.Id, version = 7 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("version_conflict");
    }

    [Fact]
    public async Task Deny_no_jwt_is_401()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Patch, new Uri(TaskCyclePath(Guid.CreateVersion7()), UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { cycleId = (Guid?)null, version = 0 }),
        };
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "set-task-cycle is deny-by-default (FR-068)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
    }

    [Fact]
    public async Task The_flattened_today_rows_carry_cycle_id_and_carried_over()
    {
        // The slice-006 flattening gap must NOT recur (D16): the widened fields must reach the
        // flattened Today row shape too, not only TaskResponse.
        var owner = await CreateUserAsync("g-tc-tod", "tctod@example.com", "Owner");
        var token = TokenFor(owner);
        var cycle = await CreateCycleAsync(token, "Sprint", Day(0), Day(14));
        var id = await SeedCycleTaskAsync(owner, "Na dziś", cycleId: cycle.Id, carriedOver: true);
        using var reschedule = await SendAsync(
            HttpMethod.Patch, $"/api/tasks/{id}/due-date", token,
            new { dueDate = DateTime.UtcNow, dueHasTime = false, version = 0 });
        reschedule.StatusCode.Should().Be(HttpStatusCode.OK);

        using var response = await SendAsync(HttpMethod.Get, "/api/tasks/today", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var today = await response.ReadTodayAsync();
        var row = today.Groups.SelectMany(g => g.Tasks).Should().ContainSingle(t => t.Id == id).Subject;
        row.CycleId.Should().Be(cycle.Id, "the flattened Today row is widened too (D16)");
        row.CarriedOver.Should().BeTrue();
    }

    [Fact]
    public async Task The_upcoming_and_list_rows_carry_cycle_id_and_carried_over()
    {
        var owner = await CreateUserAsync("g-tc-upc", "tcupc@example.com", "Owner");
        var token = TokenFor(owner);
        var cycle = await CreateCycleAsync(token, "Sprint", Day(0), Day(14));
        var id = await SeedCycleTaskAsync(owner, "Wkrótce", cycleId: cycle.Id);
        using var reschedule = await SendAsync(
            HttpMethod.Patch, $"/api/tasks/{id}/due-date", token,
            new { dueDate = DateTime.UtcNow.AddDays(3), dueHasTime = false, version = 0 });
        reschedule.StatusCode.Should().Be(HttpStatusCode.OK);

        using var upcoming = await SendAsync(HttpMethod.Get, "/api/tasks/upcoming", token);
        upcoming.StatusCode.Should().Be(HttpStatusCode.OK);
        var upcomingRow = (await upcoming.ReadUpcomingAsync()).Groups.SelectMany(g => g.Tasks)
            .Should().ContainSingle(t => t.Id == id).Subject;
        upcomingRow.CycleId.Should().Be(cycle.Id, "the Upcoming rows are widened too (D16)");

        using var list = await SendAsync(HttpMethod.Get, "/api/tasks", token);
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var listRow = (await list.ReadTaskBodiesAsync()).Should().ContainSingle(t => t.Id == id).Subject;
        listRow.CycleId.Should().Be(cycle.Id);
        listRow.CarriedOver.Should().BeFalse();
    }

    [Fact]
    public async Task The_assigned_rows_carry_cycle_id_and_carried_over()
    {
        var owner = await CreateUserAsync("g-tc-asg", "tcasg@example.com", "Owner");
        var member = await CreateUserAsync("g-tc-asm", "tcasm@example.com", "Member");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, member, MembershipRoles.Editor);
        var cycle = await CreateCycleAsync(token, "Sprint", Day(0), Day(14));
        var id = await SeedCycleTaskAsync(owner, "Przypisane", cycleId: cycle.Id, projectId: project.Id);
        using var assign = await SendAsync(
            HttpMethod.Patch, $"/api/tasks/{id}/assignees", token,
            new { assigneeIds = new[] { member.Value }, version = 0 });
        assign.StatusCode.Should().Be(HttpStatusCode.OK);

        using var response = await SendAsync(HttpMethod.Get, "/api/tasks/assigned", TokenFor(member));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var row = (await response.ReadAssignedAsync()).Groups.SelectMany(g => g.Tasks)
            .Should().ContainSingle(t => t.Id == id).Subject;
        row.CycleId.Should().Be(cycle.Id, "the Assigned rows are widened too (D16)");
        row.CarriedOver.Should().BeFalse();
    }
}
