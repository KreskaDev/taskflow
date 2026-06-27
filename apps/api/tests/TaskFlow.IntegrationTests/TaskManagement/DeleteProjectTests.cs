using System.Globalization;
using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;
using DomainProject = TaskFlow.Domain.TaskManagement.Project;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T018, US1) for <c>DELETE /api/projects/{id}</c> — the soft-delete with task +
/// child dispositions (FR-014/EC-03/AS-10, research R5). Carries <c>version</c>/<c>taskDisposition</c>/
/// <c>childDisposition</c> as QUERY params. THREE task dispositions (cascade / move_to_inbox /
/// archive_with_tasks) and TWO child dispositions (cascade / orphan_to_top), applied in-transaction BEFORE
/// the tombstone. <c>archive_with_tasks</c> ARCHIVES the project (no tombstone) keeping its tasks. The
/// delete is VERSIONED (NOT idempotent like the task delete): a stale <c>version</c> → 409.
/// </summary>
/// <remarks>
/// RED-FIRST (Constitution VIII): this file compiles and FAILS until T019 (DeleteProject command + handler
/// + the DELETE route) lands. The route does not exist yet, so the allow cases expect 204 but observe 404.
/// Tasks are seeded directly through the DbContext (the MoveTaskToProject command is slice-004 US2, out of
/// this vertical), so a project's tasks exist to exercise the dispositions.
/// </remarks>
public sealed class DeleteProjectTests : IntegrationTestBase
{
    private const string EnsurePath = "/api/users/ensure";

    private static string CreatePath(Guid id) => $"/api/projects/{id}";

    private static string DeletePath(Guid id, int version, string? taskDisposition = null, string? childDisposition = null)
    {
        var query = $"?version={version.ToString(CultureInfo.InvariantCulture)}";
        if (taskDisposition is not null)
        {
            query += $"&taskDisposition={taskDisposition}";
        }

        if (childDisposition is not null)
        {
            query += $"&childDisposition={childDisposition}";
        }

        return $"/api/projects/{id}{query}";
    }

    private async Task<UserId> CreateOwnerAsync(string sub, string email)
    {
        var profile = await (await SendAsync(
            HttpMethod.Post, EnsurePath, TestJwtHelper.Valid(sub),
            new { googleSubjectId = sub, email, displayName = "Project Owner", avatarUrl = (string?)null }))
            .ReadProfileAsync();
        return UserId.From(profile.Id);
    }

    private async Task<ProjectBody> CreateProjectAsync(string token, string name, string color, string icon, Guid? parentId = null)
    {
        var id = Guid.CreateVersion7();
        using var response = await SendAsync(HttpMethod.Put, CreatePath(id), token, new { name, color, icon, parentId });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the helper creates a valid project");
        return await response.ReadProjectAsync();
    }

    /// <summary>Seeds a task directly under <paramref name="projectId"/> (the move command is US2, out of scope here).</summary>
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

    private async Task<DomainProject?> LoadProjectAsync(Guid id)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == ProjectId.From(id));
    }

    private async Task<TaskEntity?> LoadTaskAsync(Guid id)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tasks.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == TaskId.From(id));
    }

    [Fact]
    public async Task Allow_delete_a_childless_taskless_project_tombstones_it_204()
    {
        var owner = await CreateOwnerAsync("google-sub-dp-plain", "dpplain@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var project = await CreateProjectAsync(token, "Doomed", "blue", "folder");

        using var response = await SendAsync(HttpMethod.Delete, DeletePath(project.Id, project.Version), token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "deleting a childless/taskless project returns 204");
        var stored = await LoadProjectAsync(project.Id);
        stored.Should().NotBeNull("soft-delete keeps the row; only the reaper hard-deletes it");
        stored!.DeletedAt.Should().NotBeNull("delete stamps deleted_at (the tombstone)");
    }

    [Fact]
    public async Task Allow_task_disposition_cascade_soft_deletes_the_projects_tasks()
    {
        var owner = await CreateOwnerAsync("google-sub-dp-tcascade", "dptcascade@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var project = await CreateProjectAsync(token, "With tasks", "blue", "folder");
        var taskId = await SeedTaskUnderProjectAsync(owner, project.Id, "Task", "a0");

        using var response = await SendAsync(
            HttpMethod.Delete, DeletePath(project.Id, project.Version, taskDisposition: "cascade"), token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoadProjectAsync(project.Id))!.DeletedAt.Should().NotBeNull("the project is tombstoned");
        (await LoadTaskAsync(taskId))!.DeletedAt.Should().NotBeNull("taskDisposition=cascade soft-deletes the project's tasks (R5)");
    }

    [Fact]
    public async Task Allow_task_disposition_move_to_inbox_nulls_project_id_keeping_tasks_live()
    {
        var owner = await CreateOwnerAsync("google-sub-dp-tinbox", "dptinbox@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var project = await CreateProjectAsync(token, "With tasks", "blue", "folder");
        var taskId = await SeedTaskUnderProjectAsync(owner, project.Id, "Task", "a0");

        using var response = await SendAsync(
            HttpMethod.Delete, DeletePath(project.Id, project.Version, taskDisposition: "move_to_inbox"), token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoadProjectAsync(project.Id))!.DeletedAt.Should().NotBeNull("the project is tombstoned");
        var task = await LoadTaskAsync(taskId);
        task!.DeletedAt.Should().BeNull("move_to_inbox keeps the task LIVE");
        task.ProjectId.Should().BeNull("move_to_inbox nulls project_id, returning the task to the Inbox (FR-021)");
    }

    [Fact]
    public async Task Allow_task_disposition_archive_with_tasks_archives_the_project_no_tombstone()
    {
        // R5: archive_with_tasks ARCHIVES the project (no tombstone) and keeps its tasks projected.
        var owner = await CreateOwnerAsync("google-sub-dp-tarchive", "dptarchive@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var project = await CreateProjectAsync(token, "With tasks", "blue", "folder");
        var taskId = await SeedTaskUnderProjectAsync(owner, project.Id, "Task", "a0");

        using var response = await SendAsync(
            HttpMethod.Delete, DeletePath(project.Id, project.Version, taskDisposition: "archive_with_tasks"), token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "archive_with_tasks resolves to an archive (no tombstone), still a 204");
        var stored = await LoadProjectAsync(project.Id);
        stored!.DeletedAt.Should().BeNull("archive_with_tasks does NOT tombstone the project (R5)");
        stored.ArchivedAt.Should().NotBeNull("archive_with_tasks archives the project instead of deleting it");
        var task = await LoadTaskAsync(taskId);
        task!.DeletedAt.Should().BeNull("the tasks are kept");
        task.ProjectId.Should().Be(ProjectId.From(project.Id), "the tasks stay projected under the archived project");
    }

    [Fact]
    public async Task Validation_delete_a_project_with_tasks_without_task_disposition_is_422()
    {
        // FR-014/EC-03: a project WITH tasks REQUIRES a task disposition → omitting it is a 422.
        var owner = await CreateOwnerAsync("google-sub-dp-notdisp", "dpnotdisp@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var project = await CreateProjectAsync(token, "With tasks", "blue", "folder");
        _ = await SeedTaskUnderProjectAsync(owner, project.Id, "Task", "a0");

        using var response = await SendAsync(HttpMethod.Delete, DeletePath(project.Id, project.Version), token);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a project-with-tasks delete REQUIRES a task disposition (FR-014/EC-03)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
        (await LoadProjectAsync(project.Id))!.DeletedAt.Should().BeNull("the rejected delete never tombstoned the project");
    }

    [Fact]
    public async Task Allow_child_disposition_cascade_soft_deletes_children_with_the_parent()
    {
        var owner = await CreateOwnerAsync("google-sub-dp-ccascade", "dpccascade@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var parent = await CreateProjectAsync(token, "Parent", "blue", "folder");
        var child = await CreateProjectAsync(token, "Child", "green", "star", parentId: parent.Id);

        using var response = await SendAsync(
            HttpMethod.Delete, DeletePath(parent.Id, parent.Version, childDisposition: "cascade"), token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoadProjectAsync(parent.Id))!.DeletedAt.Should().NotBeNull("the parent is tombstoned");
        (await LoadProjectAsync(child.Id))!.DeletedAt.Should().NotBeNull("childDisposition=cascade soft-deletes the children with the parent (R5)");
    }

    [Fact]
    public async Task Allow_child_disposition_orphan_to_top_promotes_children_keeping_them_live()
    {
        var owner = await CreateOwnerAsync("google-sub-dp-corphan", "dpcorphan@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var parent = await CreateProjectAsync(token, "Parent", "blue", "folder");
        var child = await CreateProjectAsync(token, "Child", "green", "star", parentId: parent.Id);

        using var response = await SendAsync(
            HttpMethod.Delete, DeletePath(parent.Id, parent.Version, childDisposition: "orphan_to_top"), token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoadProjectAsync(parent.Id))!.DeletedAt.Should().NotBeNull("the parent is tombstoned");
        var storedChild = await LoadProjectAsync(child.Id);
        storedChild!.DeletedAt.Should().BeNull("orphan_to_top keeps the child LIVE");
        storedChild.ParentId.Should().BeNull("orphan_to_top promotes the child to top-level (AS-10)");
    }

    [Fact]
    public async Task Validation_delete_a_parent_with_children_without_child_disposition_is_422()
    {
        var owner = await CreateOwnerAsync("google-sub-dp-nocdisp", "dpnocdisp@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var parent = await CreateProjectAsync(token, "Parent", "blue", "folder");
        _ = await CreateProjectAsync(token, "Child", "green", "star", parentId: parent.Id);

        using var response = await SendAsync(HttpMethod.Delete, DeletePath(parent.Id, parent.Version), token);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a parent-with-children delete REQUIRES a child disposition (AS-10)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
        (await LoadProjectAsync(parent.Id))!.DeletedAt.Should().BeNull("the rejected delete never tombstoned the parent");
    }

    [Fact]
    public async Task Allow_delete_a_parent_whose_only_child_is_archived_needs_no_child_disposition()
    {
        // FR-049/R5: the child disposition concerns only ACTIVE (visible) children. A parent whose only
        // child is ARCHIVED has zero active children, so deleting it requires NO childDisposition — the
        // client cannot see or count the archived child, so demanding one would be an unactionable 422.
        var owner = await CreateOwnerAsync("google-sub-dp-archchild", "dparchchild@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var parent = await CreateProjectAsync(token, "Parent", "blue", "folder");
        var child = await CreateProjectAsync(token, "Child", "green", "star", parentId: parent.Id);

        using (var archive = await SendAsync(
            HttpMethod.Patch, $"/api/projects/{child.Id}/archive", token, new { version = child.Version }))
        {
            archive.StatusCode.Should().Be(HttpStatusCode.OK, "archiving the leaf child succeeds");
        }

        // Delete the parent with NO childDisposition — must succeed (its only child is archived/hidden).
        using var response = await SendAsync(HttpMethod.Delete, DeletePath(parent.Id, parent.Version), token);

        response.StatusCode.Should().Be(
            HttpStatusCode.NoContent,
            "a parent whose only child is archived needs no child disposition (active children = 0)");
        (await LoadProjectAsync(parent.Id))!.DeletedAt.Should().NotBeNull("the parent is tombstoned");
        var storedChild = await LoadProjectAsync(child.Id);
        storedChild!.DeletedAt.Should().BeNull("the archived child is left untouched — it follows its own lifecycle (R9/FK)");
        storedChild.ArchivedAt.Should().NotBeNull("the archived child stays archived");
    }

    [Fact]
    public async Task Allow_archive_with_tasks_and_child_cascade_archives_the_whole_subtree()
    {
        // R5: when the parent's fate is ARCHIVE (archive_with_tasks), childDisposition=cascade ARCHIVES the
        // children too — the whole subtree shares the reversible disposition, never a mix.
        var owner = await CreateOwnerAsync("google-sub-dp-subtree", "dpsubtree@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var parent = await CreateProjectAsync(token, "Parent", "blue", "folder");
        var child = await CreateProjectAsync(token, "Child", "green", "star", parentId: parent.Id);
        var taskId = await SeedTaskUnderProjectAsync(owner, parent.Id, "Task", "a0");

        using var response = await SendAsync(
            HttpMethod.Delete, DeletePath(parent.Id, parent.Version, taskDisposition: "archive_with_tasks", childDisposition: "cascade"), token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var storedParent = await LoadProjectAsync(parent.Id);
        storedParent!.DeletedAt.Should().BeNull("archive_with_tasks archives, never tombstones");
        storedParent.ArchivedAt.Should().NotBeNull("the parent is archived");
        var storedChild = await LoadProjectAsync(child.Id);
        storedChild!.DeletedAt.Should().BeNull("the cascaded child follows the parent's ARCHIVE fate, not a delete");
        storedChild.ArchivedAt.Should().NotBeNull("childDisposition=cascade ARCHIVES the children when the parent is archived (R5)");
        (await LoadTaskAsync(taskId))!.DeletedAt.Should().BeNull("the tasks are kept under the archived subtree");
    }

    [Fact]
    public async Task Tombstone_excluded_from_queries_after_delete()
    {
        // The tombstoned project is excluded from the owner-scoped finds (the create idempotent-replay path
        // proves it: a fresh PUT of the SAME id resolves to 404 — the id is spent, R2).
        var owner = await CreateOwnerAsync("google-sub-dp-excluded", "dpexcluded@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var project = await CreateProjectAsync(token, "Doomed", "blue", "folder");

        using (var del = await SendAsync(HttpMethod.Delete, DeletePath(project.Id, project.Version), token))
        {
            del.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // A fresh PUT to the same (now-tombstoned) id is 404 — the id is spent, not an idempotent hit.
        using var recreate = await SendAsync(
            HttpMethod.Put, CreatePath(project.Id), token, new { name = "Reborn", color = "red", icon = "star", parentId = (Guid?)null });
        recreate.StatusCode.Should().Be(HttpStatusCode.NotFound, "a tombstoned id is spent — excluded from queries, re-create uses a fresh id (R2)");
    }

    [Fact]
    public async Task Stale_version_is_rejected_409_not_idempotent()
    {
        // R5/data-model §2: unlike the version-free task delete, project delete is VERSIONED — a stale
        // version → 409, NEVER a 204 replay.
        var owner = await CreateOwnerAsync("google-sub-dp-stale", "dpstale@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var project = await CreateProjectAsync(token, "Doomed", "blue", "folder");

        using var response = await SendAsync(HttpMethod.Delete, DeletePath(project.Id, project.Version + 99), token);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "project delete is versioned (NOT idempotent) — a stale version is 409");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("version_conflict");
        (await LoadProjectAsync(project.Id))!.DeletedAt.Should().BeNull("the rejected delete never tombstoned the project");
    }

    [Fact]
    public async Task Deny_deleting_another_users_project_is_404_not_found()
    {
        var owner = await CreateOwnerAsync("google-sub-dp-deny-owner", "dpdenyowner@example.com");
        var stranger = await CreateOwnerAsync("google-sub-dp-deny-stranger", "dpdenystranger@example.com");
        var project = await CreateProjectAsync(TestJwtHelper.Valid(owner.Value.ToString()), "Owner's", "blue", "folder");

        using var response = await SendAsync(
            HttpMethod.Delete, DeletePath(project.Id, project.Version), TestJwtHelper.Valid(stranger.Value.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a foreign id is not_found (404), never 403");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
        (await LoadProjectAsync(project.Id))!.DeletedAt.Should().BeNull("the foreign delete never tombstoned the owner's row");
    }

    [Fact]
    public async Task Deny_no_jwt_is_rejected_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(DeletePath(Guid.CreateVersion7(), 0), UriKind.Relative));
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "deleteProject is deny-by-default (FR-068)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
    }
}
