using System.Net;
using FluentAssertions;
using TaskFlow.IntegrationTests.Infrastructure;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T034, US-12) for the shared-project READ dispatch (research R8/R9). A shared
/// project's tasks are readable by any current member (viewer+); a non-member → 404. <c>GET /api/projects</c>
/// (GetMyProjects) includes the shared projects the caller is a member of and populates
/// <c>ProjectResponse.role</c> with the caller's effective role.
/// </summary>
public sealed class SharedProjectReadAccessTests : SharingTestBase
{
    [Fact]
    public async Task Allow_a_viewer_member_reads_the_shared_projects_tasks()
    {
        var owner = await CreateUserAsync("g-srt-o", "srto@example.com", "Owner A");
        var viewer = await CreateUserAsync("g-srt-vw", "srtvw@example.com", "Viewer C");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);
        await SeedTaskUnderProjectAsync(owner, project.Id, "Owner's task", "a0");

        using var response = await SendAsync(HttpMethod.Get, $"/api/projects/{project.Id}/tasks", TokenFor(viewer));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a viewer member may read the shared project's tasks (R9)");
        (await response.ReadTasksAsync()).Should().ContainSingle(t => t.Title == "Owner's task");
    }

    [Fact]
    public async Task Deny_a_non_member_reading_the_tasks_is_404()
    {
        var owner = await CreateUserAsync("g-srt-nmo", "srtnmo@example.com", "Owner A");
        var stranger = await CreateUserAsync("g-srt-nmx", "srtnmx@example.com", "Stranger X");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedTaskUnderProjectAsync(owner, project.Id, "Owner's task", "a0");

        using var response = await SendAsync(HttpMethod.Get, $"/api/projects/{project.Id}/tasks", TokenFor(stranger));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a non-member is not told the shared project exists (R9)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task GetMyProjects_includes_shared_projects_with_the_callers_role()
    {
        var owner = await CreateUserAsync("g-srt-mo", "srtmo@example.com", "Owner A");
        var editor = await CreateUserAsync("g-srt-med", "srtmed@example.com", "Editor B");
        var token = TokenFor(owner);
        var shared = await ShareProjectAsync(token, await CreateProjectAsync(token, name: "Shared"));
        await SeedMembershipAsync(shared.Id, editor, MembershipRoles.Editor);

        // The editor also owns a personal project of their own — it must come back as role=owner.
        var editorToken = TokenFor(editor);
        var ownPersonal = await CreateProjectAsync(editorToken, name: "Editor's own");

        using var response = await SendAsync(HttpMethod.Get, "/api/projects", editorToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var projects = await response.ReadProjectsAsync();
        projects.Should().Contain(p => p.Id == shared.Id, "the shared project the caller is a member of is included (R8)");
        projects.Single(p => p.Id == shared.Id).Role.Should().Be("editor", "the caller's effective role is populated (R17)");
        projects.Single(p => p.Id == ownPersonal.Id).Role.Should().Be("owner");
    }

    [Fact]
    public async Task GetMyProjects_excludes_a_shared_project_after_the_member_leaves()
    {
        var owner = await CreateUserAsync("g-srt-lo", "srtlo@example.com", "Owner A");
        var member = await CreateUserAsync("g-srt-lm", "srtlm@example.com", "Member B");
        var token = TokenFor(owner);
        var shared = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(shared.Id, member, MembershipRoles.Editor);

        // Sanity: visible before leaving.
        using (var before = await SendAsync(HttpMethod.Get, "/api/projects", TokenFor(member)))
        {
            (await before.ReadProjectsAsync()).Should().Contain(p => p.Id == shared.Id);
        }

        using (var leave = await SendAsync(HttpMethod.Delete, $"/api/projects/{shared.Id}/membership?version={shared.Version}", TokenFor(member)))
        {
            leave.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var after = await SendAsync(HttpMethod.Get, "/api/projects", TokenFor(member));
        (await after.ReadProjectsAsync()).Should().NotContain(p => p.Id == shared.Id, "a former member no longer sees the project (R10)");
    }
}
