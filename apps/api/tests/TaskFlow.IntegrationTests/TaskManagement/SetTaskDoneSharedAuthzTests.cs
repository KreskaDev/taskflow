using System.Net;
using FluentAssertions;
using TaskFlow.IntegrationTests.Infrastructure;
using DomainTaskStatus = TaskFlow.Domain.TaskManagement.TaskStatus;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// The slice-005 shared-project arm (T044, the BLOCKER-resolved deviation, spec L127) for
/// <c>PATCH /api/tasks/{id}/status</c> — the <c>Space</c> toggle-done, now membership-aware. An editor member
/// MAY complete a shared task; a viewer member is denied 403; a non-member is denied 404. The PERSONAL path
/// is unchanged and proven by the slice-002 <see cref="SetTaskDoneTests"/> (the additive-regression suite),
/// which runs untouched.
/// </summary>
public sealed class SetTaskDoneSharedAuthzTests : SharingTestBase
{
    private static string StatusPath(Guid id) => $"/api/tasks/{id}/status";

    [Fact]
    public async Task Allow_an_editor_member_completes_a_shared_task()
    {
        var owner = await CreateUserAsync("g-sd-so", "sdso@example.com", "Owner");
        var editor = await CreateUserAsync("g-sd-sed", "sdsed@example.com", "Editor");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);

        using var response = await SendAsync(HttpMethod.Patch, StatusPath(id), TokenFor(editor), new { status = "done", version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "an editor member may complete a shared-project task (FR-067)");
        (await response.ReadTaskAsync()).Status.Should().Be("done");
        (await LoadTaskAsync(id))!.Status.Should().Be(DomainTaskStatus.Done);
    }

    [Fact]
    public async Task Deny_a_viewer_member_is_403()
    {
        var owner = await CreateUserAsync("g-sd-vo", "sdvo@example.com", "Owner");
        var viewer = await CreateUserAsync("g-sd-vw", "sdvw@example.com", "Viewer");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);

        using var response = await SendAsync(HttpMethod.Patch, StatusPath(id), TokenFor(viewer), new { status = "done", version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a viewer may read but not complete a shared task (FR-067)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
        (await LoadTaskAsync(id))!.Status.Should().Be(DomainTaskStatus.Backlog, "the viewer's toggle never landed");
    }

    [Fact]
    public async Task Deny_a_non_member_is_404()
    {
        var owner = await CreateUserAsync("g-sd-no", "sdno@example.com", "Owner");
        var stranger = await CreateUserAsync("g-sd-nx", "sdnx@example.com", "Stranger");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);

        using var response = await SendAsync(HttpMethod.Patch, StatusPath(id), TokenFor(stranger), new { status = "done", version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a non-member is not told the shared task exists");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }
}
