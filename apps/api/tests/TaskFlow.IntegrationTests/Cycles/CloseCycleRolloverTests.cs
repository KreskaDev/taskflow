using System.Net;
using FluentAssertions;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests.Cycles;

/// <summary>
/// The D4/D10 close+rollover matrix (slice 011 T003, contracts/cycles-api.md, data-model.md):
/// close is legal only from active; the bulk rollover choice applies to EVERY incomplete task
/// team-wide (including tasks the caller cannot see), caller-visible <c>overrides</c> win per
/// task, the whole operation (status flip + moves + carried-over flags) commits in ONE
/// transaction, and the response carries the counts for the confirmation copy.
/// </summary>
public sealed class CloseCycleRolloverTests : CyclesTestBase
{
    [Theory]
    [InlineData(false)] // planned
    [InlineData(true)]  // closed
    public async Task Deny_close_from_a_non_active_status_is_422_cycle_not_active(bool close)
    {
        var user = await CreateUserAsync($"g-cy-cna{close}", $"cycna{close}@example.com", "Closer");
        var token = TokenFor(user);
        var cycle = await CreateCycleAsync(token, "Sprint", Day(0), Day(14));
        if (close)
        {
            var active = await ActivateCycleAsync(token, cycle);
            using var first = await SendAsync(HttpMethod.Patch, ClosePath(active.Id), token, new { version = active.Version });
            first.StatusCode.Should().Be(HttpStatusCode.OK);
            cycle = (await first.ReadCloseCycleAsync()).Cycle;
        }

        using var response = await SendAsync(HttpMethod.Patch, ClosePath(cycle.Id), token, new { version = cycle.Version });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "close is legal only from active");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("cycle_not_active");
    }

    [Fact]
    public async Task Rollover_next_reassigns_all_incomplete_tasks_including_invisible_ones()
    {
        var closer = await CreateUserAsync("g-cy-nx1", "cynx1@example.com", "Closer");
        var other = await CreateUserAsync("g-cy-nx2", "cynx2@example.com", "Other");
        var token = TokenFor(closer);
        var active = await ActivateCycleAsync(token, await CreateCycleAsync(token, "Bieżący", Day(0), Day(14)));
        var next = await CreateCycleAsync(token, "Następny", Day(14), Day(28));
        var mine = await SeedCycleTaskAsync(closer, "Moje niedokończone", cycleId: active.Id, status: "todo");
        var invisible = await SeedCycleTaskAsync(other, "Cudze niedokończone", cycleId: active.Id, status: "in_progress");
        var done = await SeedCycleTaskAsync(closer, "Zrobione", cycleId: active.Id, status: "done");

        using var response = await SendAsync(
            HttpMethod.Patch, ClosePath(active.Id), token, new { rollover = "next", version = active.Version });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadCloseCycleAsync();
        body.Cycle.Status.Should().Be("closed");
        body.RolledToNext.Should().Be(2, "the bulk default covers the OTHER user's incomplete task too (D10)");
        body.RolledToBacklog.Should().Be(0);
        body.Kept.Should().Be(0);

        (await LoadTaskCycleStateAsync(mine)).Should().Be(((Guid?)next.Id, false));
        (await LoadTaskCycleStateAsync(invisible)).Should().Be(((Guid?)next.Id, false), "the team-wide bulk move includes invisible tasks");
        (await LoadTaskCycleStateAsync(done)).Should().Be(((Guid?)active.Id, false), "completed tasks stay as the closed cycle's record");
    }

    [Fact]
    public async Task Rollover_next_targets_the_planned_cycle_lowest_by_start_then_created_at()
    {
        var closer = await CreateUserAsync("g-cy-nx3", "cynx3@example.com", "Closer");
        var token = TokenFor(closer);
        var active = await ActivateCycleAsync(token, await CreateCycleAsync(token, "Bieżący", Day(0), Day(14)));
        var later = await CreateCycleAsync(token, "Późniejszy", Day(28), Day(42));
        var firstOfTie = await CreateCycleAsync(token, "Pierwszy z remisu", Day(14), Day(28));
        var secondOfTie = await CreateCycleAsync(token, "Drugi z remisu", Day(14), Day(28));
        var task = await SeedCycleTaskAsync(closer, "Do przeniesienia", cycleId: active.Id, status: "todo");

        using var response = await SendAsync(
            HttpMethod.Patch, ClosePath(active.Id), token, new { rollover = "next", version = active.Version });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadTaskCycleStateAsync(task)).CycleId.Should().Be(
            firstOfTie.Id,
            "next = lowest (StartDate, CreatedAt, Id) among planned cycles (D5) — {0} and {1} lose",
            secondOfTie.Id, later.Id);
    }

    [Fact]
    public async Task Rollover_backlog_clears_the_assignments()
    {
        var closer = await CreateUserAsync("g-cy-bl", "cybl@example.com", "Closer");
        var token = TokenFor(closer);
        var active = await ActivateCycleAsync(token, await CreateCycleAsync(token, "Bieżący", Day(0), Day(14)));
        var a = await SeedCycleTaskAsync(closer, "A", cycleId: active.Id, status: "todo");
        var b = await SeedCycleTaskAsync(closer, "B", cycleId: active.Id, status: "backlog");
        var done = await SeedCycleTaskAsync(closer, "Zrobione", cycleId: active.Id, status: "done");

        using var response = await SendAsync(
            HttpMethod.Patch, ClosePath(active.Id), token, new { rollover = "backlog", version = active.Version });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadCloseCycleAsync();
        body.RolledToBacklog.Should().Be(2);
        body.RolledToNext.Should().Be(0);
        body.Kept.Should().Be(0);
        (await LoadTaskCycleStateAsync(a)).Should().Be(((Guid?)null, false), "backlog = no cycle assignment (FR-016)");
        (await LoadTaskCycleStateAsync(b)).Should().Be(((Guid?)null, false));
        (await LoadTaskCycleStateAsync(done)).CycleId.Should().Be(active.Id, "completed tasks are untouched");
    }

    [Fact]
    public async Task Rollover_keep_flags_incomplete_tasks_only()
    {
        var closer = await CreateUserAsync("g-cy-kp", "cykp@example.com", "Closer");
        var token = TokenFor(closer);
        var active = await ActivateCycleAsync(token, await CreateCycleAsync(token, "Bieżący", Day(0), Day(14)));
        var incomplete = await SeedCycleTaskAsync(closer, "Niedokończone", cycleId: active.Id, status: "in_progress");
        var done = await SeedCycleTaskAsync(closer, "Zrobione", cycleId: active.Id, status: "done");
        var cancelled = await SeedCycleTaskAsync(closer, "Anulowane", cycleId: active.Id, status: "cancelled");

        using var response = await SendAsync(
            HttpMethod.Patch, ClosePath(active.Id), token, new { rollover = "keep", version = active.Version });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadCloseCycleAsync();
        body.Kept.Should().Be(1, "done/cancelled tasks are not outstanding work");
        (await LoadTaskCycleStateAsync(incomplete)).Should().Be(((Guid?)active.Id, true), "keep flags carried_over (D7)");
        (await LoadTaskCycleStateAsync(done)).Should().Be(((Guid?)active.Id, false), "a completed task is never flagged");
        (await LoadTaskCycleStateAsync(cancelled)).Should().Be(((Guid?)active.Id, false));
    }

    [Fact]
    public async Task Overrides_apply_per_task_and_the_bulk_default_covers_the_rest()
    {
        var closer = await CreateUserAsync("g-cy-ovr", "cyovr@example.com", "Closer");
        var token = TokenFor(closer);
        var active = await ActivateCycleAsync(token, await CreateCycleAsync(token, "Bieżący", Day(0), Day(14)));
        var next = await CreateCycleAsync(token, "Następny", Day(14), Day(28));
        var bulked = await SeedCycleTaskAsync(closer, "Objęte domyślną", cycleId: active.Id, status: "todo");
        var toBacklog = await SeedCycleTaskAsync(closer, "Do backlogu", cycleId: active.Id, status: "todo");
        var kept = await SeedCycleTaskAsync(closer, "Zostaje", cycleId: active.Id, status: "todo");

        using var response = await SendAsync(
            HttpMethod.Patch, ClosePath(active.Id), token, new
            {
                rollover = "next",
                overrides = new[]
                {
                    new { taskId = toBacklog, choice = "backlog" },
                    new { taskId = kept, choice = "keep" },
                },
                version = active.Version,
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadCloseCycleAsync();
        (body.RolledToNext, body.RolledToBacklog, body.Kept).Should().Be((1, 1, 1));
        (await LoadTaskCycleStateAsync(bulked)).Should().Be(((Guid?)next.Id, false));
        (await LoadTaskCycleStateAsync(toBacklog)).Should().Be(((Guid?)null, false));
        (await LoadTaskCycleStateAsync(kept)).Should().Be(((Guid?)active.Id, true));
    }

    [Fact]
    public async Task Deny_an_override_on_an_invisible_task_is_404_and_nothing_partial_lands()
    {
        var closer = await CreateUserAsync("g-cy-ovx1", "cyovx1@example.com", "Closer");
        var other = await CreateUserAsync("g-cy-ovx2", "cyovx2@example.com", "Other");
        var token = TokenFor(closer);
        var active = await ActivateCycleAsync(token, await CreateCycleAsync(token, "Bieżący", Day(0), Day(14)));
        await CreateCycleAsync(token, "Następny", Day(14), Day(28));
        var mine = await SeedCycleTaskAsync(closer, "Moje", cycleId: active.Id, status: "todo");
        var foreign = await SeedCycleTaskAsync(other, "Cudze osobiste", cycleId: active.Id, status: "todo");

        using var response = await SendAsync(
            HttpMethod.Patch, ClosePath(active.Id), token, new
            {
                rollover = "next",
                overrides = new[] { new { taskId = foreign, choice = "backlog" } },
                version = active.Version,
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "overrides may target only caller-visible tasks (404 posture, D10)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");

        // Transactionality: the whole command is rejected — the status flip rolled back and no task moved.
        using var list = await SendAsync(HttpMethod.Get, "/api/cycles", token);
        (await list.ReadCyclesAsync()).Should().Contain(
            c => c.Id == active.Id && c.Status == "active", "the failing override rolls back the close");
        (await LoadTaskCycleStateAsync(mine)).CycleId.Should().Be(active.Id, "nothing partial lands");
        (await LoadTaskCycleStateAsync(foreign)).CycleId.Should().Be(active.Id);
    }

    [Fact]
    public async Task Deny_rollover_next_with_no_planned_cycle_is_422_no_next_cycle()
    {
        var closer = await CreateUserAsync("g-cy-nonext", "cynonext@example.com", "Closer");
        var token = TokenFor(closer);
        var active = await ActivateCycleAsync(token, await CreateCycleAsync(token, "Jedyny", Day(0), Day(14)));
        var task = await SeedCycleTaskAsync(closer, "Niedokończone", cycleId: active.Id, status: "todo");

        using var response = await SendAsync(
            HttpMethod.Patch, ClosePath(active.Id), token, new { rollover = "next", version = active.Version });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "US-05.AS-06: prompt to create a cycle first");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("no_next_cycle");

        using var list = await SendAsync(HttpMethod.Get, "/api/cycles", token);
        (await list.ReadCyclesAsync()).Should().Contain(c => c.Id == active.Id && c.Status == "active", "nothing closed");
        (await LoadTaskCycleStateAsync(task)).CycleId.Should().Be(active.Id);
    }

    [Fact]
    public async Task A_close_with_zero_incomplete_tasks_is_a_pure_close()
    {
        var closer = await CreateUserAsync("g-cy-pure", "cypure@example.com", "Closer");
        var token = TokenFor(closer);
        var active = await ActivateCycleAsync(token, await CreateCycleAsync(token, "Czysty", Day(0), Day(14)));
        var done = await SeedCycleTaskAsync(closer, "Zrobione", cycleId: active.Id, status: "done");

        using var response = await SendAsync(HttpMethod.Patch, ClosePath(active.Id), token, new { version = active.Version });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "no rollover field is needed for a pure close");
        var body = await response.ReadCloseCycleAsync();
        body.Cycle.Status.Should().Be("closed");
        (body.RolledToNext, body.RolledToBacklog, body.Kept).Should().Be((0, 0, 0));
        (await LoadTaskCycleStateAsync(done)).Should().Be(((Guid?)active.Id, false));
    }

    [Fact]
    public async Task Deny_close_with_a_stale_version_is_409()
    {
        var closer = await CreateUserAsync("g-cy-clst", "cyclst@example.com", "Closer");
        var token = TokenFor(closer);
        var active = await ActivateCycleAsync(token, await CreateCycleAsync(token, "Sprint", Day(0), Day(14)));

        using var response = await SendAsync(HttpMethod.Patch, ClosePath(active.Id), token, new { version = 9 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("version_conflict");
    }
}
