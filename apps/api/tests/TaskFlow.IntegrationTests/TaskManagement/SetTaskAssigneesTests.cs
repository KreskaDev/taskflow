using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskFlow.IntegrationTests.Infrastructure;
using Wolverine;
using Wolverine.Tracking;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;
using TaskAssigned = TaskFlow.Domain.TaskManagement.Events.TaskAssigned;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T010, US1) for <c>PATCH /api/tasks/{id}/assignees</c> (operationId
/// <c>setTaskAssignees</c>, slice 008). Whole-set replace, shared-only, editor/owner, assignee-must-be-a-
/// current-member. Authorizes by dispatch-by-visibility; raises <c>TaskAssigned</c> (delta + actor) on a
/// real change. The full SC-016 deny matrix: viewer → 403, non-member → 404, personal task → 404,
/// non-member assignee → 422, stale version → 409.
/// </summary>
public sealed class SetTaskAssigneesTests : SharingTestBase
{
    private static string AssigneesPath(Guid id) => $"/api/tasks/{id}/assignees";

    [Fact]
    public async Task Allow_an_editor_assigns_members_and_raises_TaskAssigned()
    {
        var owner = await CreateUserAsync("g-as-o", "aso@example.com", "Owner");
        var editor = await CreateUserAsync("g-as-ed", "ased@example.com", "Editor");
        var viewer = await CreateUserAsync("g-as-vw", "asvw@example.com", "Viewer");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);

        var host = Services.GetRequiredService<IHost>();
        ITrackedSession tracked = await host.TrackActivity().Timeout(TimeSpan.FromSeconds(10)).ExecuteAndWaitAsync(
            _ => SendAsync(HttpMethod.Patch, AssigneesPath(id), TokenFor(editor),
                new { assigneeIds = new[] { editor.Value, viewer.Value }, version = 0 }));

        var ev = tracked.Sent.MessagesOf<TaskAssigned>().Should().ContainSingle().Subject;
        ev.AddedAssigneeIds.Select(u => u.Value).Should().BeEquivalentTo([editor.Value, viewer.Value]);
        ev.ActorUserId.Value.Should().Be(editor.Value);
        (await LoadTaskAsync(id))!.Assignees.Select(a => a.UserId.Value).Should().BeEquivalentTo([editor.Value, viewer.Value]);
    }

    [Fact]
    public async Task Allow_self_assignment()
    {
        var owner = await CreateUserAsync("g-as-so", "asso@example.com", "Owner");
        var editor = await CreateUserAsync("g-as-sed", "assed@example.com", "Editor");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);

        using var response = await SendAsync(HttpMethod.Patch, AssigneesPath(id), TokenFor(editor),
            new { assigneeIds = new[] { editor.Value }, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a member may assign themselves (spec scenario 6)");
        (await response.ReadTaskAsync()).Assignees.Should().BeEquivalentTo([editor.Value]);
    }

    [Fact]
    public async Task Idempotent_no_op_set_raises_no_event_and_does_not_bump_version()
    {
        var owner = await CreateUserAsync("g-as-idem", "asidem@example.com", "Owner");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);
        await SendAsync(HttpMethod.Patch, AssigneesPath(id), token, new { assigneeIds = new[] { owner.Value }, version = 0 });
        var versionAfterFirst = (await LoadTaskAsync(id))!.Version;

        var host = Services.GetRequiredService<IHost>();
        var tracked = await host.TrackActivity().Timeout(TimeSpan.FromSeconds(10)).ExecuteAndWaitAsync(
            _ => SendAsync(HttpMethod.Patch, AssigneesPath(id), token, new { assigneeIds = new[] { owner.Value }, version = versionAfterFirst }));

        tracked.Sent.MessagesOf<TaskAssigned>().Should().BeEmpty("a no-op set raises no event (idempotency, R3)");
        (await LoadTaskAsync(id))!.Version.Should().Be(versionAfterFirst, "a no-op set does not bump version");
    }

    [Fact]
    public async Task Deny_a_viewer_is_403()
    {
        var owner = await CreateUserAsync("g-as-vo", "asvo@example.com", "Owner");
        var viewer = await CreateUserAsync("g-as-vv", "asvv@example.com", "Viewer");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);

        using var response = await SendAsync(HttpMethod.Patch, AssigneesPath(id), TokenFor(viewer),
            new { assigneeIds = new[] { viewer.Value }, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a viewer may read but not assign (FR-067)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
    }

    [Fact]
    public async Task Deny_a_non_member_is_404()
    {
        var owner = await CreateUserAsync("g-as-no", "asno@example.com", "Owner");
        var stranger = await CreateUserAsync("g-as-nx", "asnx@example.com", "Stranger");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);

        using var response = await SendAsync(HttpMethod.Patch, AssigneesPath(id), TokenFor(stranger),
            new { assigneeIds = new[] { stranger.Value }, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_a_personal_task_is_404()
    {
        // FR-069: a personal/Inbox task has no assignment surface → 404.
        var owner = await CreateUserAsync("g-as-pers", "aspers@example.com", "Owner");
        var id = await SeedTaskAsync(owner, "Personal inbox task"); // no project = Inbox

        using var response = await SendAsync(HttpMethod.Patch, AssigneesPath(id), TokenFor(owner),
            new { assigneeIds = new[] { owner.Value }, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "assignment is offered only on shared-project tasks");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_a_non_member_assignee_is_422()
    {
        var owner = await CreateUserAsync("g-as-ma-o", "asmao@example.com", "Owner");
        var stranger = await CreateUserAsync("g-as-ma-x", "asmax@example.com", "Stranger");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);

        using var response = await SendAsync(HttpMethod.Patch, AssigneesPath(id), token,
            new { assigneeIds = new[] { stranger.Value }, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "an assignee must be a current member (FR-069)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
        (await LoadTaskAsync(id))!.Assignees.Should().BeEmpty("the rejected assignment never landed");
    }

    [Fact]
    public async Task Deny_a_stale_version_is_409()
    {
        var owner = await CreateUserAsync("g-as-stale", "asstale@example.com", "Owner");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);

        using var response = await SendAsync(HttpMethod.Patch, AssigneesPath(id), token,
            new { assigneeIds = new[] { owner.Value }, version = 9 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("version_conflict");
    }
}
