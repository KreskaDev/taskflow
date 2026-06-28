using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskFlow.IntegrationTests.Infrastructure;
using Wolverine;
using Wolverine.Tracking;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Assignment-cleanup coverage (T017, US1, R5): assignment must not outlive membership. Unshare clears ALL;
/// remove/leave clears that user (event-driven via the slice-007 ProjectUnshared / MembershipRevoked
/// consumers); a role DEMOTION keeps assignments (a viewer is still a member); DeleteProject + move_to_inbox
/// clears (the bulk-move path, inline). The event-driven clears run on the in-process durable queue, so the
/// trigger is wrapped in TrackActivity to await the handler.
/// </summary>
public sealed class AssignmentCleanupTests : SharingTestBase
{
    private async Task AssignAsync(string ownerToken, Guid taskId, params Guid[] assigneeIds)
    {
        using var r = await SendAsync(HttpMethod.Patch, $"/api/tasks/{taskId}/assignees", ownerToken, new { assigneeIds, version = 0 });
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<IReadOnlyList<Guid>> AssigneeIdsAsync(Guid taskId) =>
        (await LoadTaskAsync(taskId))!.Assignees.Select(a => a.UserId.Value).ToList();

    [Fact]
    public async Task Unshare_clears_all_assignees()
    {
        var owner = await CreateUserAsync("g-cl-uo", "cluo@example.com", "Owner");
        var editor = await CreateUserAsync("g-cl-ue", "club@example.com", "Editor");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);
        await AssignAsync(token, id, owner.Value, editor.Value);

        var version = (await LoadProjectAsync(project.Id))!.Version;
        var host = Services.GetRequiredService<IHost>();
        await host.TrackActivity().Timeout(TimeSpan.FromSeconds(10)).ExecuteAndWaitAsync(
            _ => SendAsync(HttpMethod.Patch, $"/api/projects/{project.Id}/unshare", token, new { version }));

        (await AssigneeIdsAsync(id)).Should().BeEmpty("unshare reverts to personal — all assignees cleared (R5)");
    }

    [Fact]
    public async Task Remove_member_clears_that_users_assignments_only()
    {
        var owner = await CreateUserAsync("g-cl-ro", "clro@example.com", "Owner");
        var editor = await CreateUserAsync("g-cl-re", "clre@example.com", "Editor");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);
        await AssignAsync(token, id, owner.Value, editor.Value);

        var version = (await LoadProjectAsync(project.Id))!.Version;
        var host = Services.GetRequiredService<IHost>();
        await host.TrackActivity().Timeout(TimeSpan.FromSeconds(10)).ExecuteAndWaitAsync(
            _ => SendAsync(HttpMethod.Delete, $"/api/projects/{project.Id}/members/{editor.Value}?version={version}", token));

        var remaining = await AssigneeIdsAsync(id);
        remaining.Should().NotContain(editor.Value, "the removed member's assignment is cleared (R5)");
        remaining.Should().Contain(owner.Value, "other assignees are untouched");
    }

    [Fact]
    public async Task Demotion_to_viewer_keeps_assignments()
    {
        var owner = await CreateUserAsync("g-cl-do", "cldo@example.com", "Owner");
        var editor = await CreateUserAsync("g-cl-de", "clde@example.com", "Editor");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);
        await AssignAsync(token, id, editor.Value);

        var version = (await LoadProjectAsync(project.Id))!.Version;
        var host = Services.GetRequiredService<IHost>();
        await host.TrackActivity().Timeout(TimeSpan.FromSeconds(10)).ExecuteAndWaitAsync(
            _ => SendAsync(HttpMethod.Patch, $"/api/projects/{project.Id}/members/{editor.Value}", token, new { role = "viewer", version }));

        (await AssigneeIdsAsync(id)).Should().Contain(editor.Value, "a demoted-to-viewer user is still a member — assignment kept (R5)");
    }

    [Fact]
    public async Task Delete_project_move_to_inbox_clears_assignees()
    {
        var owner = await CreateUserAsync("g-cl-delo", "cldelo@example.com", "Owner");
        var editor = await CreateUserAsync("g-cl-dele", "cldele@example.com", "Editor");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);
        await AssignAsync(token, id, owner.Value, editor.Value);

        var version = (await LoadProjectAsync(project.Id))!.Version;
        using var response = await SendAsync(HttpMethod.Delete,
            $"/api/projects/{project.Id}?version={version}&taskDisposition=move_to_inbox", token);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await AssigneeIdsAsync(id)).Should().BeEmpty("the task moved to the Inbox (personal) — assignees cleared (R5)");
    }

    [Fact]
    public async Task Edit_moving_the_task_to_the_inbox_clears_assignees()
    {
        // The /edit whole-object replace can also move a task; moving a shared task to the Inbox makes it
        // personal → assignees cleared (FR-069), persisted via the owned-collection delete.
        var owner = await CreateUserAsync("g-cl-edo", "cledo@example.com", "Owner");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);
        await AssignAsync(token, id, owner.Value);
        var version = (await LoadTaskAsync(id))!.Version;

        using var response = await SendAsync(HttpMethod.Patch, $"/api/tasks/{id}/edit", token,
            new { title = "Shared task", description = (string?)null, priority = (string?)null, dueDate = (DateTime?)null, dueHasTime = (bool?)null, projectId = (string?)null, version });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await AssigneeIdsAsync(id)).Should().BeEmpty("a /edit move to the Inbox clears assignees (FR-069)");
    }
}
