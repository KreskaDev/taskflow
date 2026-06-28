using System.Net;
using FluentAssertions;
using TaskFlow.IntegrationTests.Infrastructure;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Eager-load coverage (T008a, R7): because <c>TaskResponse.assignees</c> is a required field, a shared task
/// must carry its assignees through a NORMAL (non-assigned) read path too — here the project task list. The
/// owned collection auto-loads with the task; this guards against a silent empty array.
/// </summary>
public sealed class TaskAssigneesEagerLoadTests : SharingTestBase
{
    [Fact]
    public async Task A_shared_task_carries_its_assignees_in_the_project_task_list()
    {
        var owner = await CreateUserAsync("g-el-o", "elo@example.com", "Owner");
        var editor = await CreateUserAsync("g-el-e", "ele@example.com", "Editor");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);
        using (var assign = await SendAsync(HttpMethod.Patch, $"/api/tasks/{id}/assignees", token,
            new { assigneeIds = new[] { editor.Value }, version = 0 }))
        {
            assign.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var response = await SendAsync(HttpMethod.Get, $"/api/projects/{project.Id}/tasks", TokenFor(editor));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var task = (await response.ReadTaskBodiesAsync()).Single(t => t.Id == id);
        task.Assignees.Should().NotBeNull().And.Contain(editor.Value, "assignees are eager-loaded on every task read path (R7)");
    }
}
