using System.Net;
using FluentAssertions;
using TaskFlow.IntegrationTests.Infrastructure;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.Labels;

/// <summary>
/// The caller-scoped <c>TaskResponse.labels</c> projection across the read paths (slice 006, R6): every read
/// carries the caller's labels (incl. the flattened Today DTO — the gap the required-From-parameter doesn't
/// auto-cover); a co-member's labels are absent (caller-scoping); and labels SURVIVE a project move (no
/// clear-on-move, R5).
/// </summary>
public sealed class TaskLabelsReadTests : SharingTestBase
{
    private async Task<Guid> CreateLabelAsync(string token, string name)
    {
        var id = Guid.CreateVersion7();
        using var response = await SendAsync(HttpMethod.Put, $"/api/labels/{id}", token, new { name });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return id;
    }

    private async Task ApplyLabelsAsync(string token, Guid taskId, params Guid[] labelIds)
    {
        using var response = await SendAsync(HttpMethod.Patch, $"/api/tasks/{taskId}/labels", token, new { labelIds });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Inbox_read_carries_the_callers_labels()
    {
        var user = await CreateUserAsync("g-rd-ib", "rdib@example.com", "Owner");
        var token = TokenFor(user);
        var label = await CreateLabelAsync(token, "Urgent");
        var taskId = await SeedTaskAsync(user, "Inbox task");
        await ApplyLabelsAsync(token, taskId, label);

        using var response = await SendAsync(HttpMethod.Get, "/api/tasks", token);

        var task = (await response.ReadTaskBodiesAsync()).Should().ContainSingle(t => t.Id == taskId).Subject;
        task.Labels.Should().BeEquivalentTo([label]);
    }

    [Fact]
    public async Task Today_flattened_read_carries_the_callers_labels()
    {
        var user = await CreateUserAsync("g-rd-td", "rdtd@example.com", "Owner");
        var token = TokenFor(user);
        var label = await CreateLabelAsync(token, "Urgent");
        var taskId = await SeedTaskAsync(user, "Due-today task", dueDate: DateTime.UtcNow, dueHasTime: true);
        await ApplyLabelsAsync(token, taskId, label);

        using var response = await SendAsync(HttpMethod.Get, "/api/tasks/today", token);

        var today = await response.ReadTodayAsync();
        var row = today.Groups.SelectMany(g => g.Tasks).Should().ContainSingle(t => t.Id == taskId).Subject;
        row.Labels.Should().BeEquivalentTo([label], "the flattened TodayTaskResponse carries labels too (R6)");
    }

    [Fact]
    public async Task A_co_members_labels_are_absent_from_the_callers_read()
    {
        var owner = await CreateUserAsync("g-rd-iso-o", "rdisoo@example.com", "Owner");
        var editor = await CreateUserAsync("g-rd-iso-e", "rdisoe@example.com", "Editor");
        var ownerToken = TokenFor(owner);
        var project = await ShareProjectAsync(ownerToken, await CreateProjectAsync(ownerToken));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        var taskId = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);

        var ownerLabel = await CreateLabelAsync(ownerToken, "OwnerTag");
        var editorLabel = await CreateLabelAsync(TokenFor(editor), "EditorTag");
        await ApplyLabelsAsync(ownerToken, taskId, ownerLabel);
        await ApplyLabelsAsync(TokenFor(editor), taskId, editorLabel);

        using var ownerView = await SendAsync(HttpMethod.Get, $"/api/projects/{project.Id}/tasks", ownerToken);
        using var editorView = await SendAsync(HttpMethod.Get, $"/api/projects/{project.Id}/tasks", TokenFor(editor));

        (await ownerView.ReadTaskBodiesAsync()).Single(t => t.Id == taskId).Labels.Should().BeEquivalentTo([ownerLabel]);
        (await editorView.ReadTaskBodiesAsync()).Single(t => t.Id == taskId).Labels.Should().BeEquivalentTo([editorLabel],
            "each member sees ONLY their own labels on the shared task (caller-scoped)");
    }

    [Fact]
    public async Task Labels_survive_a_project_move()
    {
        var user = await CreateUserAsync("g-rd-mv", "rdmv@example.com", "Owner");
        var token = TokenFor(user);
        var label = await CreateLabelAsync(token, "Keep");
        var taskId = await SeedTaskAsync(user, "Movable task");
        await ApplyLabelsAsync(token, taskId, label);
        var project = await CreateProjectAsync(token);

        // The label set replace is versionless, so the seeded task is still version 0 for the move.
        using var move = await SendAsync(HttpMethod.Patch, $"/api/tasks/{taskId}/project", token, new { projectId = project.Id, version = 0 });
        move.StatusCode.Should().Be(HttpStatusCode.OK);
        (await move.ReadTaskAsync()).Labels.Should().BeEquivalentTo([label], "labels are project-independent — a move does not clear them (R5)");

        using var projectView = await SendAsync(HttpMethod.Get, $"/api/projects/{project.Id}/tasks", token);
        (await projectView.ReadTaskBodiesAsync()).Single(t => t.Id == taskId).Labels.Should().BeEquivalentTo([label]);
    }
}
