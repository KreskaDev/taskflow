using System.Net;
using FluentAssertions;
using TaskFlow.IntegrationTests.Infrastructure;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.Cycles;

/// <summary>
/// Read-side coverage (slice 011 T004): <c>GET /api/cycles</c> (D5 ordering incl. the tiebreaker;
/// TEAM-WIDE computed metrics excluding soft-deleted rows — D6/D10), <c>GET /api/cycles/{id}/tasks</c>
/// (caller-visibility filtering per the FR-065 dispatch; EC-12 archived-project inclusion), and the
/// D8 preference surface (<c>PATCH /api/users/me/preferences</c> + the widened profile).
/// </summary>
public sealed class CycleReadTests : CyclesTestBase
{
    [Fact]
    public async Task List_orders_cycles_by_start_date_then_created_at_then_id()
    {
        var user = await CreateUserAsync("g-cr-ord", "crord@example.com", "Reader");
        var token = TokenFor(user);
        var laterStart = await CreateCycleAsync(token, "Późniejszy start", Day(10), Day(24));
        var earlyStart = await CreateCycleAsync(token, "Wczesny start", Day(5), Day(19));
        var tieSecond = await CreateCycleAsync(token, "Remis — drugi", Day(10), Day(24));

        using var response = await SendAsync(HttpMethod.Get, "/api/cycles", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cycles = await response.ReadCyclesAsync();
        cycles.Select(c => c.Id).Should().Equal(
            [earlyStart.Id, laterStart.Id, tieSecond.Id],
            "ordering is (StartDate, CreatedAt, Id) — D5; equal starts break on CreatedAt");
        cycles.Should().OnlyContain(c => c.CreatedAt != default, "createdAt is exposed for the client-side D5 tiebreaker");
    }

    [Fact]
    public async Task Metrics_are_team_wide_and_exclude_soft_deleted_tasks()
    {
        var caller = await CreateUserAsync("g-cr-tw1", "crtw1@example.com", "Caller");
        var other = await CreateUserAsync("g-cr-tw2", "crtw2@example.com", "Other");
        var token = TokenFor(caller);
        var cycle = await CreateCycleAsync(token, "Zespołowy", Day(0), Day(14));
        await SeedCycleTaskAsync(caller, "Moje", cycleId: cycle.Id, status: "todo");
        await SeedCycleTaskAsync(other, "Cudze — liczy się", cycleId: cycle.Id, status: "done");
        var deleted = await SeedCycleTaskAsync(caller, "Skasowane — nie liczy się", cycleId: cycle.Id, status: "todo");
        await SoftDeleteTaskRowAsync(deleted);

        using var response = await SendAsync(HttpMethod.Get, "/api/cycles", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.ReadCyclesAsync()).Should().ContainSingle(c => c.Id == cycle.Id).Subject;
        body.Metrics.Total.Should().Be(2, "the numbers are TEAM-WIDE (other users' tasks count) and tombstones are excluded");
        body.Metrics.Done.Should().Be(1, "the other user's done task counts");
    }

    [Fact]
    public async Task Metrics_break_down_by_every_status()
    {
        var user = await CreateUserAsync("g-cr-bd", "crbd@example.com", "Reader");
        var token = TokenFor(user);
        var cycle = await CreateCycleAsync(token, "Pełny przekrój", Day(0), Day(14));
        await SeedCycleTaskAsync(user, "B", cycleId: cycle.Id, status: "backlog");
        await SeedCycleTaskAsync(user, "T", cycleId: cycle.Id, status: "todo");
        await SeedCycleTaskAsync(user, "I", cycleId: cycle.Id, status: "in_progress");
        await SeedCycleTaskAsync(user, "D", cycleId: cycle.Id, status: "done");
        await SeedCycleTaskAsync(user, "C", cycleId: cycle.Id, status: "cancelled");

        using var response = await SendAsync(HttpMethod.Get, "/api/cycles", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var metrics = (await response.ReadCyclesAsync()).Should().ContainSingle(c => c.Id == cycle.Id).Subject.Metrics;
        metrics.Total.Should().Be(5);
        metrics.Done.Should().Be(1);
        metrics.Breakdown.Should().Be(new CycleBreakdownBody(1, 1, 1, 1, 1));
    }

    [Fact]
    public async Task Cycle_tasks_are_filtered_to_the_callers_visibility()
    {
        var caller = await CreateUserAsync("g-cr-vis1", "crvis1@example.com", "Caller");
        var other = await CreateUserAsync("g-cr-vis2", "crvis2@example.com", "Other");
        var callerToken = TokenFor(caller);
        var otherToken = TokenFor(other);
        var cycle = await CreateCycleAsync(callerToken, "Widoczność", Day(0), Day(14));

        var own = await SeedCycleTaskAsync(caller, "Własne osobiste", cycleId: cycle.Id, position: "a0");
        await SeedCycleTaskAsync(other, "Cudze osobiste — niewidoczne", cycleId: cycle.Id, position: "a1");
        var sharedProject = await ShareProjectAsync(otherToken, await CreateProjectAsync(otherToken));
        await SeedMembershipAsync(sharedProject.Id, caller, MembershipRoles.Viewer);
        var sharedTask = await SeedCycleTaskAsync(other, "Wspólne — widoczne", cycleId: cycle.Id, projectId: sharedProject.Id, position: "a2");

        using var response = await SendAsync(HttpMethod.Get, CycleTasksPath(cycle.Id), callerToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await response.ReadTaskBodiesAsync();
        rows.Select(t => t.Id).Should().Equal(
            [own, sharedTask],
            "own personal + current-membership shared tasks, ordered by position; a foreign personal task is invisible (FR-065/D10)");
    }

    [Fact]
    public async Task Cycle_tasks_exclude_a_former_members_shared_rows()
    {
        var caller = await CreateUserAsync("g-cr-fm1", "crfm1@example.com", "Caller");
        var owner = await CreateUserAsync("g-cr-fm2", "crfm2@example.com", "Owner");
        var callerToken = TokenFor(caller);
        var ownerToken = TokenFor(owner);
        var cycle = await CreateCycleAsync(callerToken, "Były członek", Day(0), Day(14));
        var project = await ShareProjectAsync(ownerToken, await CreateProjectAsync(ownerToken));
        await SeedMembershipAsync(project.Id, caller, MembershipRoles.Editor);
        var sharedTask = await SeedCycleTaskAsync(owner, "Wspólne", cycleId: cycle.Id, projectId: project.Id);

        using var before = await SendAsync(HttpMethod.Get, CycleTasksPath(cycle.Id), callerToken);
        (await before.ReadTaskBodiesAsync()).Should().Contain(t => t.Id == sharedTask, "a current member sees the row");

        await DeleteMembershipRowAsync(project.Id, caller);

        using var after = await SendAsync(HttpMethod.Get, CycleTasksPath(cycle.Id), callerToken);
        (await after.ReadTaskBodiesAsync()).Should().NotContain(
            t => t.Id == sharedTask, "membership loss revokes ALL access (FR-066)");
    }

    [Fact]
    public async Task Cycle_tasks_include_archived_project_rows()
    {
        // EC-12: archiving a project hides its tasks from project-based views, but they REMAIN
        // visible in the Cycle view.
        var user = await CreateUserAsync("g-cr-arch", "crarch@example.com", "Archiver");
        var token = TokenFor(user);
        var cycle = await CreateCycleAsync(token, "Z archiwum", Day(0), Day(14));
        var project = await CreateProjectAsync(token, name: "Do archiwum");
        var task = await SeedCycleTaskAsync(user, "W cyklu mimo archiwum", cycleId: cycle.Id, projectId: project.Id);
        await ArchiveProjectAsync(token, project);

        using var response = await SendAsync(HttpMethod.Get, CycleTasksPath(cycle.Id), token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadTaskBodiesAsync()).Should().Contain(t => t.Id == task, "EC-12: the row stays in the Cycle view");
    }

    [Fact]
    public async Task Cycle_tasks_of_an_unknown_cycle_is_404()
    {
        var user = await CreateUserAsync("g-cr-404", "cr404@example.com", "Reader");

        using var response = await SendAsync(HttpMethod.Get, CycleTasksPath(Guid.CreateVersion7()), TokenFor(user));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Preferences_default_and_roundtrip()
    {
        var user = await CreateUserAsync("g-cr-pref", "crpref@example.com", "Preferer");
        var token = TokenFor(user);

        using var initial = await SendAsync(HttpMethod.Get, "/api/users/me", token);
        initial.StatusCode.Should().Be(HttpStatusCode.OK);
        (await initial.ReadProfileAsync()).CycleDefaultDurationDays.Should().Be(14, "the D8 default is 2 weeks");

        using var patch = await SendAsync(
            HttpMethod.Patch, "/api/users/me/preferences", token, new { cycleDefaultDurationDays = 21 });
        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        (await patch.ReadProfileAsync()).CycleDefaultDurationDays.Should().Be(21, "the PATCH returns the widened profile");

        using var reread = await SendAsync(HttpMethod.Get, "/api/users/me", token);
        (await reread.ReadProfileAsync()).CycleDefaultDurationDays.Should().Be(21, "the preference persists server-side");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    [InlineData(-3)]
    public async Task Deny_an_out_of_range_preference_is_422(int days)
    {
        var user = await CreateUserAsync($"g-cr-rng{days + 3}", $"crrng{days + 3}@example.com", "Preferer");
        var token = TokenFor(user);

        using var response = await SendAsync(
            HttpMethod.Patch, "/api/users/me/preferences", token, new { cycleDefaultDurationDays = days });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "the range is 1..90 (D8)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");

        using var reread = await SendAsync(HttpMethod.Get, "/api/users/me", token);
        (await reread.ReadProfileAsync()).CycleDefaultDurationDays.Should().Be(14, "the rejected write never landed");
    }

    [Fact]
    public async Task Deny_preferences_without_a_jwt_is_401()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Patch, new Uri("/api/users/me/preferences", UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { cycleDefaultDurationDays = 21 }),
        };
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
    }
}
