using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T033, US2) for <c>PATCH /api/tasks/{id}/project</c> — the <c>M</c>
/// move-to-project action (US-08.AS-05, research R7). The handler authorizes ownership of BOTH the task
/// AND (when non-null) the target project, resolving either failure to 404 (the ownership posture, R13).
/// <c>projectId = null</c> moves the task back to the Inbox (FR-021). Carries the optimistic
/// <c>version</c>; a stale token → 409.
/// </summary>
/// <remarks>
/// RED-FIRST (Constitution VIII): this file compiles and FAILS until T034 (MoveTaskToProject command +
/// handler + the PATCH /api/tasks/{id}/project route) lands — the route does not exist yet, so the allow
/// cases expect 200 but observe 404. The move's EFFECT is asserted via a direct DbContext load
/// (<see cref="LoadTaskAsync"/>) rather than the response body, because <c>TaskResponse.projectId</c> is
/// added in T037 (out of this vertical) and is not yet on the wire.
/// </remarks>
public sealed class MoveTaskToProjectTests : IntegrationTestBase
{
    private const string EnsurePath = "/api/users/ensure";

    private static string ProjectPath(Guid id) => $"/api/projects/{id}";

    private static string MovePath(Guid taskId) => $"/api/tasks/{taskId}/project";

    private async Task<UserId> CreateOwnerAsync(string sub, string email)
    {
        var profile = await (await SendAsync(
            HttpMethod.Post, EnsurePath, TestJwtHelper.Valid(sub),
            new { googleSubjectId = sub, email, displayName = "Move Owner", avatarUrl = (string?)null }))
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

    /// <summary>Seeds an Inbox task (no project) directly through the DbContext.</summary>
    private async Task<Guid> SeedInboxTaskAsync(UserId owner, string title, string position)
    {
        var id = Guid.CreateVersion7();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = TaskEntity.Create(TaskId.From(id), owner, title, position, DateTime.UtcNow);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>Seeds a task already projected under <paramref name="projectId"/> directly through the DbContext.</summary>
    private async Task<Guid> SeedTaskUnderProjectAsync(UserId owner, Guid projectId, string title, string position)
    {
        var id = Guid.CreateVersion7();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = TaskEntity.Create(TaskId.From(id), owner, title, position, DateTime.UtcNow);
        db.Entry(task).Property(nameof(TaskEntity.ProjectId)).CurrentValue = ProjectId.From(projectId);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<TaskEntity?> LoadTaskAsync(Guid id)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tasks.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == TaskId.From(id));
    }

    [Fact]
    public async Task Allow_move_an_inbox_task_to_an_owned_project_sets_project_id_200()
    {
        var owner = await CreateOwnerAsync("google-sub-mv-allow", "mvallow@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var project = await CreateProjectAsync(token, "Target", "blue", "folder");
        var taskId = await SeedInboxTaskAsync(owner, "Move me", "a0");

        using var response = await SendAsync(
            HttpMethod.Patch, MovePath(taskId), token, new { projectId = project.Id, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "moving an owned task to an owned project succeeds");
        var stored = await LoadTaskAsync(taskId);
        stored!.ProjectId.Should().Be(ProjectId.From(project.Id), "the move sets project_id (R7)");
        stored.Version.Should().Be(1, "the move is a mutation and bumps version");
    }

    [Fact]
    public async Task Allow_move_a_projected_task_to_the_inbox_with_null_clears_project_id_200()
    {
        // R7: a null target returns the task to the Inbox (the inverse of FR-021).
        var owner = await CreateOwnerAsync("google-sub-mv-inbox", "mvinbox@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var project = await CreateProjectAsync(token, "Source", "blue", "folder");
        var taskId = await SeedTaskUnderProjectAsync(owner, project.Id, "Send home", "a0");

        using var response = await SendAsync(
            HttpMethod.Patch, MovePath(taskId), token, new { projectId = (Guid?)null, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "moving a task to the Inbox (null) succeeds");
        var stored = await LoadTaskAsync(taskId);
        stored!.ProjectId.Should().BeNull("a null target clears project_id, returning the task to the Inbox (FR-021)");
        stored.Version.Should().Be(1);
    }

    [Fact]
    public async Task Deny_moving_another_users_task_is_404_not_found()
    {
        // Both-ownership posture (R7): a FOREIGN TASK → 404, never 403.
        var owner = await CreateOwnerAsync("google-sub-mv-deny-task-owner", "mvdenytaskowner@example.com");
        var stranger = await CreateOwnerAsync("google-sub-mv-deny-task-stranger", "mvdenytaskstranger@example.com");
        var strangerToken = TestJwtHelper.Valid(stranger.Value.ToString());
        var strangerProject = await CreateProjectAsync(strangerToken, "Stranger's", "blue", "folder");
        var taskId = await SeedInboxTaskAsync(owner, "Owner's task", "a0");

        using var response = await SendAsync(
            HttpMethod.Patch, MovePath(taskId), strangerToken, new { projectId = strangerProject.Id, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a foreign task is not_found (404), never 403");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
        (await LoadTaskAsync(taskId))!.ProjectId.Should().BeNull("the foreign move never touched the owner's task");
    }

    [Fact]
    public async Task Deny_moving_into_another_users_project_is_404_not_found()
    {
        // Both-ownership posture (R7): a FOREIGN TARGET PROJECT → 404 (existence not disclosed). The caller
        // owns the task but the target project is the stranger's, so the move must not file it there.
        var owner = await CreateOwnerAsync("google-sub-mv-deny-proj-owner", "mvdenyprojowner@example.com");
        var stranger = await CreateOwnerAsync("google-sub-mv-deny-proj-stranger", "mvdenyprojstranger@example.com");
        var ownerToken = TestJwtHelper.Valid(owner.Value.ToString());
        var strangerProject = await CreateProjectAsync(TestJwtHelper.Valid(stranger.Value.ToString()), "Stranger's", "blue", "folder");
        var taskId = await SeedInboxTaskAsync(owner, "My task", "a0");

        using var response = await SendAsync(
            HttpMethod.Patch, MovePath(taskId), ownerToken, new { projectId = strangerProject.Id, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a foreign target project is not_found (404), never 403");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
        (await LoadTaskAsync(taskId))!.ProjectId.Should().BeNull("a task can never be filed under another user's project (R7)");
    }

    [Fact]
    public async Task Stale_version_is_rejected_409_version_conflict()
    {
        var owner = await CreateOwnerAsync("google-sub-mv-stale", "mvstale@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var project = await CreateProjectAsync(token, "Target", "blue", "folder");
        var taskId = await SeedInboxTaskAsync(owner, "Move me", "a0");

        using var response = await SendAsync(
            HttpMethod.Patch, MovePath(taskId), token, new { projectId = project.Id, version = 99 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "a stale version is 409 version_conflict (R7)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("version_conflict");
        (await LoadTaskAsync(taskId))!.ProjectId.Should().BeNull("the rejected move never touched the task");
    }

    [Fact]
    public async Task Deny_no_jwt_is_rejected_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri(MovePath(Guid.CreateVersion7()), UriKind.Relative))
        {
            Content = JsonContent.Create(new { projectId = (Guid?)null, version = 0 }),
        };
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "moveTaskToProject is deny-by-default (FR-068)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
    }
}
