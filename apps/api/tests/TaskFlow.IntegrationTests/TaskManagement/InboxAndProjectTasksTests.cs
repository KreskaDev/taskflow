using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T035, US2) for the Inbox narrowing and the project task list (research R6):
/// <list type="bullet">
/// <item><c>GET /api/tasks</c> is the Inbox — narrowed to <c>project_id IS NULL</c> (FR-021); pre-existing
/// (unprojected) tasks still appear, projected tasks drop out, preserving <c>ORDER BY position, id</c>.</item>
/// <item><c>GET /api/projects/{id}/tasks</c> returns that project's tasks (owner + project scoped); a
/// foreign/absent project → 404 (the ownership posture, R13), NOT a 200 empty list (no existence leak).</item>
/// </list>
/// </summary>
/// <remarks>
/// RED-FIRST (Constitution VIII): the <c>GET /api/projects/{id}/tasks</c> route does not exist yet and
/// <c>GetMyTasks</c> is not yet narrowed, so the project-tasks cases (404/list) and the Inbox-exclusion case
/// fail until T036 lands. Tasks are seeded/projected directly through the DbContext (the move command is
/// exercised in <c>MoveTaskToProjectTests</c>).
/// </remarks>
public sealed class InboxAndProjectTasksTests : IntegrationTestBase
{
    private const string TasksPath = "/api/tasks";
    private const string EnsurePath = "/api/users/ensure";

    private static string ProjectPath(Guid id) => $"/api/projects/{id}";

    private static string ProjectTasksPath(Guid id) => $"/api/projects/{id}/tasks";

    private async Task<UserId> CreateOwnerAsync(string sub, string email)
    {
        var profile = await (await SendAsync(
            HttpMethod.Post, EnsurePath, TestJwtHelper.Valid(sub),
            new { googleSubjectId = sub, email, displayName = "Inbox Owner", avatarUrl = (string?)null }))
            .ReadProfileAsync();
        return UserId.From(profile.Id);
    }

    private async Task<ProjectBody> CreateProjectAsync(string token, string name, string color, string icon)
    {
        var id = Guid.CreateVersion7();
        using var response = await SendAsync(HttpMethod.Put, ProjectPath(id), token, new { name, color, icon, parentId = (Guid?)null });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the helper creates a valid project");
        return await response.ReadProjectAsync();
    }

    /// <summary>Seeds a task, optionally projected under <paramref name="projectId"/>, directly through the DbContext.</summary>
    private async Task SeedTaskAsync(UserId owner, string title, string position, Guid? projectId = null)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = TaskEntity.Create(TaskId.From(Guid.CreateVersion7()), owner, title, position, DateTime.UtcNow);
        if (projectId is { } pid)
        {
            db.Entry(task).Property(nameof(TaskEntity.ProjectId)).CurrentValue = ProjectId.From(pid);
        }

        db.Tasks.Add(task);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Inbox_returns_only_unprojected_tasks_preserving_position_order()
    {
        var owner = await CreateOwnerAsync("google-sub-inbox-narrow", "inboxnarrow@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var project = await CreateProjectAsync(token, "Work", "blue", "folder");

        // Two Inbox tasks (seeded out of position order) + one projected task that must NOT appear.
        await SeedTaskAsync(owner, "Inbox second", "a2");
        await SeedTaskAsync(owner, "Inbox first", "a0");
        await SeedTaskAsync(owner, "In project", "a1", projectId: project.Id);

        using var response = await SendAsync(HttpMethod.Get, TasksPath, token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tasks = await response.ReadTasksAsync();

        tasks.Select(t => t.Title).Should().ContainInOrder("Inbox first", "Inbox second");
        tasks.Should().NotContain(t => t.Title == "In project", "the Inbox is narrowed to project_id IS NULL (FR-021/R6)");
        tasks.Select(t => t.Position).Should().ContainInOrder("a0", "a2");
    }

    [Fact]
    public async Task Inbox_keeps_pre_existing_unprojected_tasks_visible()
    {
        // Backward-compat: every pre-slice-004 task has project_id NULL, so it stays in the Inbox (R6).
        var owner = await CreateOwnerAsync("google-sub-inbox-compat", "inboxcompat@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        await SeedTaskAsync(owner, "Legacy task", "a0");

        using var response = await SendAsync(HttpMethod.Get, TasksPath, token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tasks = await response.ReadTasksAsync();
        tasks.Should().ContainSingle(t => t.Title == "Legacy task");
    }

    [Fact]
    public async Task Allow_project_tasks_returns_only_that_projects_tasks_ordered_by_position()
    {
        var owner = await CreateOwnerAsync("google-sub-pt-allow", "ptallow@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var project = await CreateProjectAsync(token, "Target", "blue", "folder");
        var other = await CreateProjectAsync(token, "Other", "green", "star");

        await SeedTaskAsync(owner, "P second", "a2", projectId: project.Id);
        await SeedTaskAsync(owner, "P first", "a0", projectId: project.Id);
        await SeedTaskAsync(owner, "Inbox task", "a1");
        await SeedTaskAsync(owner, "Other project task", "a0", projectId: other.Id);

        using var response = await SendAsync(HttpMethod.Get, ProjectTasksPath(project.Id), token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tasks = await response.ReadTasksAsync();

        tasks.Select(t => t.Title).Should().ContainInOrder("P first", "P second");
        tasks.Should().NotContain(t => t.Title == "Inbox task", "the project list excludes Inbox tasks");
        tasks.Should().NotContain(t => t.Title == "Other project task", "the project list is scoped to {id}");
    }

    [Fact]
    public async Task Allow_a_soft_deleted_project_task_is_excluded()
    {
        var owner = await CreateOwnerAsync("google-sub-pt-deleted", "ptdeleted@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var project = await CreateProjectAsync(token, "Target", "blue", "folder");

        await SeedTaskAsync(owner, "Live", "a0", projectId: project.Id);
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tombstoned = TaskEntity.Create(TaskId.From(Guid.CreateVersion7()), owner, "Tombstoned", "a1", DateTime.UtcNow);
            db.Entry(tombstoned).Property(nameof(TaskEntity.ProjectId)).CurrentValue = ProjectId.From(project.Id);
            tombstoned.SoftDelete(DateTime.UtcNow);
            db.Tasks.Add(tombstoned);
            await db.SaveChangesAsync();
        }

        using var response = await SendAsync(HttpMethod.Get, ProjectTasksPath(project.Id), token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tasks = await response.ReadTasksAsync();
        tasks.Should().ContainSingle(t => t.Title == "Live");
        tasks.Should().NotContain(t => t.Title == "Tombstoned", "soft-deleted tasks are excluded (deleted_at IS NULL)");
    }

    [Fact]
    public async Task Deny_project_tasks_of_another_users_project_is_404_not_found()
    {
        // R13: a foreign project id resolves to 404 (existence not disclosed), NOT a 200 empty list.
        var owner = await CreateOwnerAsync("google-sub-pt-deny-owner", "ptdenyowner@example.com");
        var stranger = await CreateOwnerAsync("google-sub-pt-deny-stranger", "ptdenystranger@example.com");
        var project = await CreateProjectAsync(TestJwtHelper.Valid(owner.Value.ToString()), "Owner's", "blue", "folder");

        using var response = await SendAsync(
            HttpMethod.Get, ProjectTasksPath(project.Id), TestJwtHelper.Valid(stranger.Value.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a foreign project id is not_found (404), never a leaky 200 []");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_project_tasks_of_an_absent_project_is_404_not_found()
    {
        var owner = await CreateOwnerAsync("google-sub-pt-absent", "ptabsent@example.com");

        using var response = await SendAsync(
            HttpMethod.Get, ProjectTasksPath(Guid.CreateVersion7()), TestJwtHelper.Valid(owner.Value.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "an absent project id is not_found (404)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_no_jwt_on_project_tasks_is_rejected_401()
    {
        using var response = await Client.GetAsync(new Uri(ProjectTasksPath(Guid.CreateVersion7()), UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "listProjectTasks is deny-by-default (FR-068)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
    }
}
