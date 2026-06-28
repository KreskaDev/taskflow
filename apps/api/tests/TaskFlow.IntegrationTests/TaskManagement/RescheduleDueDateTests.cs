using System.Net;
using FluentAssertions;
using TaskFlow.IntegrationTests.Infrastructure;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T010/T042, US1) for <c>PATCH /api/tasks/{id}/due-date</c> (operationId
/// <c>rescheduleTaskDueDate</c>, slice 005, AS-05). The server re-validates the client-resolved instant with
/// the reused slice-003 rules (pairing invariant + UTC-kind + plausible range). Authorization is dispatched
/// by the containing project's visibility (personal → ownership; shared → editor/owner).
/// </summary>
public sealed class RescheduleDueDateTests : SharingTestBase
{
    private static readonly DateTime ValidDue = new(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);

    private static string DueDatePath(Guid id) => $"/api/tasks/{id}/due-date";

    [Fact]
    public async Task Allow_owner_reschedules_to_a_resolved_instant()
    {
        var owner = await CreateUserAsync("g-rs-a", "rsa@example.com", "Owner");
        var id = await SeedTaskAsync(owner, "Reschedule me");

        using var response = await SendAsync(HttpMethod.Patch, DueDatePath(id), TokenFor(owner),
            new { dueDate = ValidDue, dueHasTime = true, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadTaskAsync();
        body.DueDate.Should().Be(ValidDue);
        body.DueHasTime.Should().BeTrue();
        body.Version.Should().Be(1);
    }

    [Fact]
    public async Task Allow_owner_clears_the_due_date_with_both_null()
    {
        var owner = await CreateUserAsync("g-rs-clr", "rsclr@example.com", "Owner");
        var id = await SeedTaskAsync(owner, "Has a due date", dueDate: ValidDue, dueHasTime: true);

        using var response = await SendAsync(HttpMethod.Patch, DueDatePath(id), TokenFor(owner),
            new { dueDate = (DateTime?)null, dueHasTime = (bool?)null, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadTaskAsync();
        body.DueDate.Should().BeNull();
        body.DueHasTime.Should().BeNull();
    }

    [Fact]
    public async Task Deny_a_half_set_pair_is_422()
    {
        var owner = await CreateUserAsync("g-rs-pair", "rspair@example.com", "Owner");
        var id = await SeedTaskAsync(owner, "Pairing-guarded");

        using var response = await SendAsync(HttpMethod.Patch, DueDatePath(id), TokenFor(owner),
            new { dueDate = ValidDue, dueHasTime = (bool?)null, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a set dueDate with a null dueHasTime violates the pairing invariant");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task Deny_a_non_Z_kind_instant_is_422_not_500()
    {
        var owner = await CreateUserAsync("g-rs-kind", "rskind@example.com", "Owner");
        var id = await SeedTaskAsync(owner, "UTC-kind-guarded");

        // An offset-form instant binds to a non-UTC DateTime kind — rejected as 422 at the trust boundary
        // (a non-UTC kind would otherwise make Npgsql throw an unhandled 500 against the timestamptz column).
        using var response = await SendAsync(HttpMethod.Patch, DueDatePath(id), TokenFor(owner),
            new { dueDate = "2026-07-01T09:00:00+02:00", dueHasTime = true, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task Deny_an_implausible_range_is_422()
    {
        var owner = await CreateUserAsync("g-rs-range", "rsrange@example.com", "Owner");
        var id = await SeedTaskAsync(owner, "Range-guarded");

        using var response = await SendAsync(HttpMethod.Patch, DueDatePath(id), TokenFor(owner),
            new { dueDate = new DateTime(1500, 1, 1, 0, 0, 0, DateTimeKind.Utc), dueHasTime = true, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task Deny_a_stale_version_is_409()
    {
        var owner = await CreateUserAsync("g-rs-stale", "rsstale@example.com", "Owner");
        var id = await SeedTaskAsync(owner, "Concurrency-guarded");

        using var response = await SendAsync(HttpMethod.Patch, DueDatePath(id), TokenFor(owner),
            new { dueDate = ValidDue, dueHasTime = true, version = 9 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("version_conflict");
    }

    [Fact]
    public async Task Deny_another_users_personal_task_is_404()
    {
        var owner = await CreateUserAsync("g-rs-o", "rso@example.com", "Owner");
        var stranger = await CreateUserAsync("g-rs-x", "rsx@example.com", "Stranger");
        var id = await SeedTaskAsync(owner, "Owner's task");

        using var response = await SendAsync(HttpMethod.Patch, DueDatePath(id), TokenFor(stranger),
            new { dueDate = ValidDue, dueHasTime = true, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Allow_an_editor_member_reschedules_a_shared_task()
    {
        var owner = await CreateUserAsync("g-rs-so", "rsso@example.com", "Owner");
        var editor = await CreateUserAsync("g-rs-sed", "rssed@example.com", "Editor");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);

        using var response = await SendAsync(HttpMethod.Patch, DueDatePath(id), TokenFor(editor),
            new { dueDate = ValidDue, dueHasTime = true, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "an editor member may write to a shared-project task (FR-067)");
    }

    [Fact]
    public async Task Deny_a_viewer_member_is_403()
    {
        var owner = await CreateUserAsync("g-rs-vo", "rsvo@example.com", "Owner");
        var viewer = await CreateUserAsync("g-rs-vw", "rsvw@example.com", "Viewer");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);

        using var response = await SendAsync(HttpMethod.Patch, DueDatePath(id), TokenFor(viewer),
            new { dueDate = ValidDue, dueHasTime = true, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
    }

    [Fact]
    public async Task Deny_a_non_member_of_a_shared_project_is_404()
    {
        var owner = await CreateUserAsync("g-rs-no", "rsno@example.com", "Owner");
        var stranger = await CreateUserAsync("g-rs-nx", "rsnx@example.com", "Stranger");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);

        using var response = await SendAsync(HttpMethod.Patch, DueDatePath(id), TokenFor(stranger),
            new { dueDate = ValidDue, dueHasTime = true, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }
}
