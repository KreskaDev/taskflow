using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests.Cycles;

/// <summary>
/// Allow + deny coverage (slice 011 T002) for the cycle lifecycle per contracts/cycles-api.md:
/// idempotent create + validation, edit in every status under OCC, activate with the single-active
/// invariant (handler guard + the <c>ix_cycles_single_active</c> partial unique index as the
/// race-proof backstop, D3), the delete guards (EC-04/FR-019/FR-020), and deny-by-default 401s.
/// Lifecycle operations are TEAM-WIDE: any authenticated, admitted user may invoke them (spec IX).
/// </summary>
public sealed class CycleLifecycleTests : CyclesTestBase
{
    [Fact]
    public async Task Allow_create_returns_a_planned_cycle_with_zero_metrics()
    {
        var user = await CreateUserAsync("g-cy-cr", "cycr@example.com", "Creator");
        var id = Guid.CreateVersion7();

        using var response = await SendAsync(
            HttpMethod.Put, CyclePath(id), TokenFor(user),
            new { name = "  Sprint 1  ", startDate = Day(0), endDate = Day(14) });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadCycleAsync();
        body.Id.Should().Be(id);
        body.Name.Should().Be("Sprint 1", "the name is trimmed");
        body.Status.Should().Be("planned", "a fresh cycle is planned, never active");
        body.Version.Should().Be(0);
        body.Metrics.Total.Should().Be(0);
        body.Metrics.Done.Should().Be(0);
    }

    [Fact]
    public async Task Allow_create_is_idempotent_on_re_put()
    {
        var user = await CreateUserAsync("g-cy-idem", "cyidem@example.com", "Creator");
        var token = TokenFor(user);
        var id = Guid.CreateVersion7();
        var first = await CreateCycleAsync(token, "Sprint", Day(0), Day(14), id);

        using var replay = await SendAsync(
            HttpMethod.Put, CyclePath(id), token,
            new { name = "Sprint", startDate = Day(0), endDate = Day(14) });

        replay.StatusCode.Should().Be(HttpStatusCode.OK, "a re-PUT of the same id is idempotent success");
        (await replay.ReadCycleAsync()).Id.Should().Be(first.Id);

        using var list = await SendAsync(HttpMethod.Get, "/api/cycles", token);
        (await list.ReadCyclesAsync()).Should().HaveCount(1, "the replay never inserts a second row");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Deny_create_with_a_blank_name_is_422(string name)
    {
        var user = await CreateUserAsync($"g-cy-nm{name.Length}", $"cynm{name.Length}@example.com", "Creator");

        using var response = await SendAsync(
            HttpMethod.Put, CyclePath(Guid.CreateVersion7()), TokenFor(user),
            new { name, startDate = Day(0), endDate = Day(14) });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task Deny_create_with_an_oversized_name_is_422()
    {
        var user = await CreateUserAsync("g-cy-long", "cylong@example.com", "Creator");

        using var response = await SendAsync(
            HttpMethod.Put, CyclePath(Guid.CreateVersion7()), TokenFor(user),
            new { name = new string('x', 201), startDate = Day(0), endDate = Day(14) });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "the name is bounded at 200 chars");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task Deny_create_with_missing_dates_is_422()
    {
        var user = await CreateUserAsync("g-cy-nd", "cynd@example.com", "Creator");

        using var response = await SendAsync(
            HttpMethod.Put, CyclePath(Guid.CreateVersion7()), TokenFor(user),
            new { name = "Dateless" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "both dates are required");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
    }

    [Theory]
    [InlineData(0)]  // start == end
    [InlineData(-1)] // start > end
    public async Task Deny_create_where_start_is_not_before_end_is_422(int endOffset)
    {
        var user = await CreateUserAsync($"g-cy-se{endOffset + 1}", $"cyse{endOffset + 1}@example.com", "Creator");

        using var response = await SendAsync(
            HttpMethod.Put, CyclePath(Guid.CreateVersion7()), TokenFor(user),
            new { name = "Backwards", startDate = Day(7), endDate = Day(7 + endOffset) });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "start < end is the only cross-field rule");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task Allow_overlapping_date_ranges_between_cycles()
    {
        var user = await CreateUserAsync("g-cy-ov", "cyov@example.com", "Creator");
        var token = TokenFor(user);
        await CreateCycleAsync(token, "Pierwszy", Day(0), Day(14));

        using var response = await SendAsync(
            HttpMethod.Put, CyclePath(Guid.CreateVersion7()), token,
            new { name = "Nakładający się", startDate = Day(7), endDate = Day(21) });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "overlap is NOT validated (Clarifications 2026-08-16)");
    }

    [Fact]
    public async Task Allow_edit_in_every_status()
    {
        var user = await CreateUserAsync("g-cy-ed", "cyed@example.com", "Editor");
        var token = TokenFor(user);

        // planned
        var planned = await CreateCycleAsync(token, "Planowany", Day(0), Day(14));
        using var editPlanned = await SendAsync(
            HttpMethod.Patch, CyclePath(planned.Id), token,
            new { name = "Planowany 2", startDate = Day(1), endDate = Day(15), version = planned.Version });
        editPlanned.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterPlanned = await editPlanned.ReadCycleAsync();
        afterPlanned.Name.Should().Be("Planowany 2");
        afterPlanned.Version.Should().Be(planned.Version + 1, "an edit bumps the OCC token");

        // active
        var active = await ActivateCycleAsync(token, afterPlanned);
        using var editActive = await SendAsync(
            HttpMethod.Patch, CyclePath(active.Id), token,
            new { name = "Aktywny 2", startDate = Day(1), endDate = Day(16), version = active.Version });
        editActive.StatusCode.Should().Be(HttpStatusCode.OK, "edit is legal in every status");
        var afterActive = await editActive.ReadCycleAsync();

        // closed
        using var close = await SendAsync(
            HttpMethod.Patch, ClosePath(afterActive.Id), token, new { version = afterActive.Version });
        close.StatusCode.Should().Be(HttpStatusCode.OK);
        var closed = (await close.ReadCloseCycleAsync()).Cycle;
        using var editClosed = await SendAsync(
            HttpMethod.Patch, CyclePath(closed.Id), token,
            new { name = "Zamknięty 2", startDate = Day(1), endDate = Day(17), version = closed.Version });
        editClosed.StatusCode.Should().Be(HttpStatusCode.OK, "edit is legal in every status");
        (await editClosed.ReadCycleAsync()).Name.Should().Be("Zamknięty 2");
    }

    [Fact]
    public async Task Deny_edit_where_start_is_not_before_end_is_422()
    {
        var user = await CreateUserAsync("g-cy-edse", "cyedse@example.com", "Editor");
        var token = TokenFor(user);
        var cycle = await CreateCycleAsync(token, "Sprint", Day(0), Day(14));

        using var response = await SendAsync(
            HttpMethod.Patch, CyclePath(cycle.Id), token,
            new { name = "Sprint", startDate = Day(14), endDate = Day(0), version = cycle.Version });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "start < end is re-validated on edit");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task Deny_edit_with_a_stale_version_is_409()
    {
        var user = await CreateUserAsync("g-cy-edst", "cyedst@example.com", "Editor");
        var token = TokenFor(user);
        var cycle = await CreateCycleAsync(token, "Sprint", Day(0), Day(14));

        using var response = await SendAsync(
            HttpMethod.Patch, CyclePath(cycle.Id), token,
            new { name = "Sprint 2", startDate = Day(0), endDate = Day(14), version = 7 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("version_conflict");
    }

    [Fact]
    public async Task Deny_edit_of_an_unknown_cycle_is_404()
    {
        var user = await CreateUserAsync("g-cy-ed404", "cyed404@example.com", "Editor");

        using var response = await SendAsync(
            HttpMethod.Patch, CyclePath(Guid.CreateVersion7()), TokenFor(user),
            new { name = "Widmo", startDate = Day(0), endDate = Day(14), version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Allow_activate_a_planned_cycle()
    {
        var user = await CreateUserAsync("g-cy-act", "cyact@example.com", "Activator");
        var token = TokenFor(user);
        var cycle = await CreateCycleAsync(token, "Sprint", Day(0), Day(14));

        using var response = await SendAsync(HttpMethod.Patch, ActivatePath(cycle.Id), token, new { version = cycle.Version });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadCycleAsync();
        body.Status.Should().Be("active");
        body.Version.Should().Be(cycle.Version + 1);
    }

    [Fact]
    public async Task Deny_activate_an_already_active_cycle_is_422_cycle_not_planned()
    {
        var user = await CreateUserAsync("g-cy-act2", "cyact2@example.com", "Activator");
        var token = TokenFor(user);
        var active = await ActivateCycleAsync(token, await CreateCycleAsync(token, "Sprint", Day(0), Day(14)));

        using var response = await SendAsync(HttpMethod.Patch, ActivatePath(active.Id), token, new { version = active.Version });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "activation is legal only from planned");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("cycle_not_planned");
    }

    [Fact]
    public async Task Deny_activate_a_closed_cycle_is_422_cycle_not_planned()
    {
        var user = await CreateUserAsync("g-cy-act3", "cyact3@example.com", "Activator");
        var token = TokenFor(user);
        var active = await ActivateCycleAsync(token, await CreateCycleAsync(token, "Sprint", Day(0), Day(14)));
        using var close = await SendAsync(HttpMethod.Patch, ClosePath(active.Id), token, new { version = active.Version });
        close.StatusCode.Should().Be(HttpStatusCode.OK);
        var closed = (await close.ReadCloseCycleAsync()).Cycle;

        using var response = await SendAsync(HttpMethod.Patch, ActivatePath(closed.Id), token, new { version = closed.Version });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a closed cycle never re-activates");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("cycle_not_planned");
    }

    [Fact]
    public async Task Deny_a_second_active_cycle_is_409_cycle_active_conflict()
    {
        var user = await CreateUserAsync("g-cy-single", "cysingle@example.com", "Activator");
        var token = TokenFor(user);
        await ActivateCycleAsync(token, await CreateCycleAsync(token, "Aktywny", Day(0), Day(14)));
        var second = await CreateCycleAsync(token, "Drugi", Day(14), Day(28));

        using var response = await SendAsync(HttpMethod.Patch, ActivatePath(second.Id), token, new { version = second.Version });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "at most one cycle is active (single-active invariant)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("cycle_active_conflict");
    }

    [Fact]
    public async Task The_partial_unique_index_rejects_a_direct_second_active_row()
    {
        // The race-proof backstop (D3): even a write that bypasses the handler guard cannot
        // produce two active rows — ix_cycles_single_active (UNIQUE ((1)) WHERE status='active')
        // rejects it at the database. SqlState 23505 = unique_violation (a missing table would
        // be 42P01, so this fact stays RED until the migration lands).
        var user = await CreateUserAsync("g-cy-race", "cyrace@example.com", "Racer");
        var token = TokenFor(user);
        await ActivateCycleAsync(token, await CreateCycleAsync(token, "Aktywny", Day(0), Day(14)));
        var second = await CreateCycleAsync(token, "Drugi", Day(14), Day(28));

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var act = async () => await db.Database.ExecuteSqlAsync(
            $"UPDATE cycles SET status = 'active' WHERE id = {second.Id}");

        (await act.Should().ThrowAsync<PostgresException>("the partial unique index wins the race"))
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Deny_delete_an_active_cycle_is_422_cycle_active_delete_forbidden()
    {
        var user = await CreateUserAsync("g-cy-del1", "cydel1@example.com", "Deleter");
        var token = TokenFor(user);
        var active = await ActivateCycleAsync(token, await CreateCycleAsync(token, "Aktywny", Day(0), Day(14)));

        using var response = await SendAsync(HttpMethod.Delete, $"{CyclePath(active.Id)}?version={active.Version}", token);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "an active cycle must be closed first (EC-04/FR-019)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("cycle_active_delete_forbidden");

        using var list = await SendAsync(HttpMethod.Get, "/api/cycles", token);
        (await list.ReadCyclesAsync()).Should().ContainSingle(c => c.Id == active.Id, "the refusal never deletes");
    }

    [Fact]
    public async Task Deny_delete_a_non_empty_planned_cycle_is_422_cycle_not_empty()
    {
        var user = await CreateUserAsync("g-cy-del2", "cydel2@example.com", "Deleter");
        var token = TokenFor(user);
        var planned = await CreateCycleAsync(token, "Planowany", Day(0), Day(14));
        await SeedCycleTaskAsync(user, "Zadanie w cyklu", cycleId: planned.Id);

        using var response = await SendAsync(HttpMethod.Delete, $"{CyclePath(planned.Id)}?version={planned.Version}", token);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "only EMPTY planned/closed cycles delete (FR-020)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("cycle_not_empty");
    }

    [Fact]
    public async Task Deny_delete_a_non_empty_closed_cycle_is_422_cycle_not_empty()
    {
        var user = await CreateUserAsync("g-cy-del3", "cydel3@example.com", "Deleter");
        var token = TokenFor(user);
        var active = await ActivateCycleAsync(token, await CreateCycleAsync(token, "Aktywny", Day(0), Day(14)));
        await SeedCycleTaskAsync(user, "Niedokończone", cycleId: active.Id, status: "todo");
        using var close = await SendAsync(
            HttpMethod.Patch, ClosePath(active.Id), token, new { rollover = "keep", version = active.Version });
        close.StatusCode.Should().Be(HttpStatusCode.OK);
        var closed = (await close.ReadCloseCycleAsync()).Cycle;

        using var response = await SendAsync(HttpMethod.Delete, $"{CyclePath(closed.Id)}?version={closed.Version}", token);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("cycle_not_empty");
    }

    [Fact]
    public async Task Allow_delete_an_empty_planned_cycle()
    {
        var user = await CreateUserAsync("g-cy-del4", "cydel4@example.com", "Deleter");
        var token = TokenFor(user);
        var planned = await CreateCycleAsync(token, "Pusty planowany", Day(0), Day(14));

        using var response = await SendAsync(HttpMethod.Delete, $"{CyclePath(planned.Id)}?version={planned.Version}", token);

        // Hard delete; 204 No Content per the repo's delete convention (tasks/labels/comments).
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var list = await SendAsync(HttpMethod.Get, "/api/cycles", token);
        (await list.ReadCyclesAsync()).Should().NotContain(c => c.Id == planned.Id, "the row is physically removed");
    }

    [Fact]
    public async Task Allow_delete_an_empty_closed_cycle()
    {
        var user = await CreateUserAsync("g-cy-del5", "cydel5@example.com", "Deleter");
        var token = TokenFor(user);
        var active = await ActivateCycleAsync(token, await CreateCycleAsync(token, "Aktywny", Day(0), Day(14)));
        using var close = await SendAsync(HttpMethod.Patch, ClosePath(active.Id), token, new { version = active.Version });
        close.StatusCode.Should().Be(HttpStatusCode.OK);
        var closed = (await close.ReadCloseCycleAsync()).Cycle;

        using var response = await SendAsync(HttpMethod.Delete, $"{CyclePath(closed.Id)}?version={closed.Version}", token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Deny_delete_with_a_stale_version_is_409()
    {
        var user = await CreateUserAsync("g-cy-del6", "cydel6@example.com", "Deleter");
        var token = TokenFor(user);
        var planned = await CreateCycleAsync(token, "Planowany", Day(0), Day(14));

        using var response = await SendAsync(HttpMethod.Delete, $"{CyclePath(planned.Id)}?version=7", token);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("version_conflict");
    }

    [Theory]
    [InlineData("PUT", "/api/cycles/00000000-0000-0000-0000-000000000001")]
    [InlineData("PATCH", "/api/cycles/00000000-0000-0000-0000-000000000001")]
    [InlineData("PATCH", "/api/cycles/00000000-0000-0000-0000-000000000001/activate")]
    [InlineData("PATCH", "/api/cycles/00000000-0000-0000-0000-000000000001/close")]
    [InlineData("DELETE", "/api/cycles/00000000-0000-0000-0000-000000000001?version=0")]
    [InlineData("GET", "/api/cycles")]
    [InlineData("GET", "/api/cycles/00000000-0000-0000-0000-000000000001/tasks")]
    public async Task Deny_no_jwt_is_401_on_every_cycle_route(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        if (method is "PUT" or "PATCH")
        {
            request.Content = System.Net.Http.Json.JsonContent.Create(
                new { name = "X", startDate = Day(0), endDate = Day(14), version = 0 });
        }

        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "every cycle route is deny-by-default (FR-068)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
    }
}
