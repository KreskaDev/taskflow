using System.Net;
using FluentAssertions;
using TaskFlow.IntegrationTests.Infrastructure;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T012, US1) for <c>GET /api/tasks/assigned</c> (operationId <c>getAssignedToMe</c>,
/// slice 008, AS-03, FR-071). Caller-scoped: tasks across shared projects where the caller is a current
/// member (or owner) AND an assignee. Membership/ownership gates access (assignee is provenance only, R6).
/// </summary>
public sealed class GetAssignedToMeTests : SharingTestBase
{
    private const string AssignedPath = "/api/tasks/assigned";

    private async Task AssignAsync(string ownerToken, Guid taskId, params Guid[] assigneeIds)
    {
        using var response = await SendAsync(HttpMethod.Patch, $"/api/tasks/{taskId}/assignees", ownerToken,
            new { assigneeIds, version = 0 });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the helper assigns valid members");
    }

    [Fact]
    public async Task Allow_a_member_sees_their_assigned_shared_task()
    {
        var owner = await CreateUserAsync("g-am-o", "amo@example.com", "Owner");
        var editor = await CreateUserAsync("g-am-ed", "amed@example.com", "Editor");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        var id = await SeedTaskAsync(owner, "Assigned task", projectId: project.Id);
        await AssignAsync(token, id, editor.Value);

        using var response = await SendAsync(HttpMethod.Get, AssignedPath, TokenFor(editor));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var assigned = await response.ReadAssignedAsync();
        assigned.Groups.Should().ContainSingle(g => g.ProjectId == project.Id);
        assigned.Groups.SelectMany(g => g.Tasks).Select(t => t.Id).Should().Contain(id);
    }

    [Fact]
    public async Task Allow_the_owner_who_self_assigns_sees_it_via_the_owned_shared_union()
    {
        // The owner has NO membership row — the read must union owned-shared projects (R6), else this fails.
        var owner = await CreateUserAsync("g-am-self", "amself@example.com", "Owner");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        var id = await SeedTaskAsync(owner, "Owner self-assigned", projectId: project.Id);
        await AssignAsync(token, id, owner.Value);

        using var response = await SendAsync(HttpMethod.Get, AssignedPath, token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadAssignedAsync()).Groups.SelectMany(g => g.Tasks).Select(t => t.Id)
            .Should().Contain(id, "the owner sees their self-assigned shared task (owned-shared union)");
    }

    [Fact]
    public async Task Deny_a_member_who_is_not_an_assignee_sees_nothing()
    {
        var owner = await CreateUserAsync("g-am-na-o", "amnao@example.com", "Owner");
        var editor = await CreateUserAsync("g-am-na-e", "amnae@example.com", "Editor");
        var other = await CreateUserAsync("g-am-na-x", "amnax@example.com", "Other member");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        await SeedMembershipAsync(project.Id, other, MembershipRoles.Viewer);
        var id = await SeedTaskAsync(owner, "Assigned to editor", projectId: project.Id);
        await AssignAsync(token, id, editor.Value);

        using var response = await SendAsync(HttpMethod.Get, AssignedPath, TokenFor(other));

        (await response.ReadAssignedAsync()).Groups.SelectMany(g => g.Tasks).Select(t => t.Id)
            .Should().NotContain(id, "a member who is not an assignee does not see the task");
    }

    [Fact]
    public async Task Deny_after_membership_loss_the_assigned_task_disappears()
    {
        var owner = await CreateUserAsync("g-am-loss-o", "amlosso@example.com", "Owner");
        var editor = await CreateUserAsync("g-am-loss-e", "amlosse@example.com", "Editor");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        var id = await SeedTaskAsync(owner, "Assigned then left", projectId: project.Id);
        await AssignAsync(token, id, editor.Value);

        // The editor leaves — they lose membership.
        var current = await LoadProjectAsync(project.Id);
        using (var leave = await SendAsync(HttpMethod.Delete, $"/api/projects/{project.Id}/membership?version={current!.Version}", TokenFor(editor)))
        {
            leave.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var response = await SendAsync(HttpMethod.Get, AssignedPath, TokenFor(editor));
        (await response.ReadAssignedAsync()).Groups.SelectMany(g => g.Tasks).Select(t => t.Id)
            .Should().NotContain(id, "membership gates access — a former member sees nothing (assignee is provenance only)");
    }

    [Fact]
    public async Task Deny_no_jwt_is_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(AssignedPath, UriKind.Relative));
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
    }
}
