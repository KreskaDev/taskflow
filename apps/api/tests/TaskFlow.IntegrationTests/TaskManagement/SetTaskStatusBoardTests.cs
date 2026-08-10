using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;
using DomainTaskStatus = TaskFlow.Domain.TaskManagement.TaskStatus;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Slice 010 allow + deny coverage for the WIDENED <c>PATCH /api/tasks/{id}/status</c>
/// (contracts/task-status.md, research D1–D3): the desired-state write accepts the full
/// FR-003 enum (<c>backlog | todo | in_progress | done | cancelled</c>) through the renamed
/// <c>SetTaskStatus</c> command, preserving the invariant <c>completedAt</c> set iff
/// <c>status = done</c> and treating a same-status request as an idempotent no-op
/// (no version bump — data-model.md transition table).
/// </summary>
/// <remarks>
/// RED-FIRST (Constitution VIII): the current validator accepts only <c>done|backlog</c>, so
/// every widened-status case below observes <c>422 validation_failed</c> until the D2/D3
/// domain transition + validator land. The class name deliberately avoids every
/// <c>tasks-core</c> exclusion substring in ci.yml so the suite lands in that shard with no
/// workflow edit (research D12). Existing <c>SetTaskDoneTests</c>/<c>SetTaskDoneSharedAuthzTests</c>
/// keep covering the done/backlog arms unchanged.
/// </remarks>
public sealed class SetTaskStatusBoardTests : SharingTestBase
{
    private static string StatusPath(Guid id) => $"/api/tasks/{id}/status";

    /// <summary>
    /// Seeds a task in an arbitrary status directly through <see cref="AppDbContext"/> (the
    /// change-tracker property write used for the reserved columns — no command exists to reach
    /// <c>todo</c>/<c>in_progress</c>/<c>cancelled</c> before this slice's widening lands). A
    /// <c>done</c> seed goes through the domain so the completedAt invariant holds. Returns id + version.
    /// </summary>
    private async Task<(Guid Id, int Version)> SeedTaskWithStatusAsync(
        UserId createdBy, string title, DomainTaskStatus status, Guid? projectId = null, string position = "a0")
    {
        var id = Guid.CreateVersion7();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = TaskEntity.Create(TaskId.From(id), createdBy, title, position, DateTime.UtcNow);

        var entry = db.Entry(task);
        if (projectId is { } pid)
        {
            entry.Property(nameof(TaskEntity.ProjectId)).CurrentValue = ProjectId.From(pid);
        }

        if (status == DomainTaskStatus.Done)
        {
            task.SetStatus(DomainTaskStatus.Done, DateTime.UtcNow);
        }
        else if (status != DomainTaskStatus.Backlog)
        {
            entry.Property(nameof(TaskEntity.Status)).CurrentValue = status;
        }

        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return (id, task.Version);
    }

    [Fact]
    public async Task Allow_editor_moves_shared_task_todo_to_in_progress()
    {
        var owner = await CreateUserAsync("google-sub-board-owner1", "boardowner1@example.com", "Ada Owner");
        var editor = await CreateUserAsync("google-sub-board-editor1", "boardeditor1@example.com", "Ed Editor");
        var project = await CreateProjectAsync(TokenFor(owner), "Board");
        await ShareProjectAsync(TokenFor(owner), project);
        await SeedMembershipAsync(project.Id, editor, "editor");
        var (id, version) = await SeedTaskWithStatusAsync(owner, "Kanban move", DomainTaskStatus.Todo, project.Id);

        using var response = await SendAsync(
            HttpMethod.Patch, StatusPath(id), TokenFor(editor), new { status = "in_progress", version });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "an editor member may move a shared task (FR-067)");
        var body = await response.ReadTaskAsync();
        body.Status.Should().Be("in_progress");
        body.CompletedAt.Should().BeNull("a move between non-done statuses never touches completedAt");
        body.Version.Should().Be(version + 1, "a real transition bumps the optimistic-concurrency token");

        var stored = await LoadTaskAsync(id);
        stored!.Status.Should().Be(DomainTaskStatus.InProgress);
        stored.CompletedAt.Should().BeNull();
        stored.Version.Should().Be(version + 1);
    }

    [Fact]
    public async Task Allow_done_to_in_progress_clears_completedAt()
    {
        var owner = await CreateUserAsync("google-sub-board-reopen", "boardreopen@example.com", "Ada Owner");
        var (id, version) = await SeedTaskWithStatusAsync(owner, "Reopen into progress", DomainTaskStatus.Done);

        using var response = await SendAsync(
            HttpMethod.Patch, StatusPath(id), TokenFor(owner), new { status = "in_progress", version });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadTaskAsync();
        body.Status.Should().Be("in_progress");
        body.CompletedAt.Should().BeNull("leaving done clears completedAt (invariant: set iff done)");

        var stored = await LoadTaskAsync(id);
        stored!.Status.Should().Be(DomainTaskStatus.InProgress);
        stored.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Allow_in_progress_to_done_stamps_completedAt()
    {
        var owner = await CreateUserAsync("google-sub-board-finish", "boardfinish@example.com", "Ada Owner");
        var (id, version) = await SeedTaskWithStatusAsync(owner, "Finish from progress", DomainTaskStatus.InProgress);

        using var response = await SendAsync(
            HttpMethod.Patch, StatusPath(id), TokenFor(owner), new { status = "done", version });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadTaskAsync();
        body.Status.Should().Be("done");
        body.CompletedAt.Should().NotBeNull("entering done stamps completedAt (invariant: set iff done)");

        var stored = await LoadTaskAsync(id);
        stored!.Status.Should().Be(DomainTaskStatus.Done);
        stored.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Allow_same_status_request_is_an_idempotent_no_op_with_version_unchanged()
    {
        // data-model.md transition table: any → same status is a NO-OP — no Touch(), no version
        // bump (unlike the pre-widening MarkDone, which was unconditional; the old done/backlog
        // suites deliberately never asserted a bump, so both behaviors coexist test-wise).
        var owner = await CreateUserAsync("google-sub-board-noop", "boardnoop@example.com", "Ada Owner");
        var (id, version) = await SeedTaskWithStatusAsync(owner, "Already in progress", DomainTaskStatus.InProgress);

        using var response = await SendAsync(
            HttpMethod.Patch, StatusPath(id), TokenFor(owner), new { status = "in_progress", version });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a same-status desired-state write succeeds as a no-op");
        var body = await response.ReadTaskAsync();
        body.Status.Should().Be("in_progress");
        body.Version.Should().Be(version, "an idempotent no-op does NOT bump the version");

        var stored = await LoadTaskAsync(id);
        stored!.Version.Should().Be(version, "no Touch() on a same-status request");
        stored.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Allow_owner_sets_cancelled()
    {
        // D3: cancelled is an accepted API value (EC-11 E2E seeding basis) even though the Board
        // UI never offers it — the server capability follows the FR-003 entity contract.
        var owner = await CreateUserAsync("google-sub-board-cancel", "boardcancel@example.com", "Ada Owner");
        var (id, version) = await SeedTaskWithStatusAsync(owner, "Cancel me", DomainTaskStatus.Backlog);

        using var response = await SendAsync(
            HttpMethod.Patch, StatusPath(id), TokenFor(owner), new { status = "cancelled", version });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadTaskAsync();
        body.Status.Should().Be("cancelled");
        body.CompletedAt.Should().BeNull("cancelled is not done — completedAt stays null");

        var stored = await LoadTaskAsync(id);
        stored!.Status.Should().Be(DomainTaskStatus.Cancelled);
        stored.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Deny_viewer_is_rejected_403_forbidden_and_db_unchanged()
    {
        var owner = await CreateUserAsync("google-sub-board-owner2", "boardowner2@example.com", "Ada Owner");
        var viewer = await CreateUserAsync("google-sub-board-viewer", "boardviewer@example.com", "Vi Viewer");
        var project = await CreateProjectAsync(TokenFor(owner), "Board deny");
        await ShareProjectAsync(TokenFor(owner), project);
        await SeedMembershipAsync(project.Id, viewer, "viewer");
        var (id, version) = await SeedTaskWithStatusAsync(owner, "Viewer cannot move", DomainTaskStatus.Todo, project.Id);

        using var response = await SendAsync(
            HttpMethod.Patch, StatusPath(id), TokenFor(viewer), new { status = "in_progress", version });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a viewer may read but never write (FR-067)");
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("forbidden");
        problem.Status.Should().Be(403);

        var stored = await LoadTaskAsync(id);
        stored!.Status.Should().Be(DomainTaskStatus.Todo, "the denied write never mutated the row");
        stored.Version.Should().Be(version);
    }

    [Fact]
    public async Task Deny_non_member_is_rejected_404_not_found()
    {
        var owner = await CreateUserAsync("google-sub-board-owner3", "boardowner3@example.com", "Ada Owner");
        var outsider = await CreateUserAsync("google-sub-board-outsider", "boardoutsider@example.com", "Out Sider");
        var project = await CreateProjectAsync(TokenFor(owner), "Board 404");
        await ShareProjectAsync(TokenFor(owner), project);
        var (id, version) = await SeedTaskWithStatusAsync(owner, "Invisible to outsiders", DomainTaskStatus.Todo, project.Id);

        using var response = await SendAsync(
            HttpMethod.Patch, StatusPath(id), TokenFor(outsider), new { status = "in_progress", version });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "existence is undisclosed to non-members (FR-066)");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("not_found");

        var stored = await LoadTaskAsync(id);
        stored!.Status.Should().Be(DomainTaskStatus.Todo);
    }

    [Fact]
    public async Task Deny_stale_version_is_rejected_409_version_conflict()
    {
        var owner = await CreateUserAsync("google-sub-board-stale", "boardstale@example.com", "Ada Owner");
        var (id, version) = await SeedTaskWithStatusAsync(owner, "Stale move", DomainTaskStatus.Todo);

        using var response = await SendAsync(
            HttpMethod.Patch, StatusPath(id), TokenFor(owner), new { status = "in_progress", version = version + 5 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("version_conflict");

        var stored = await LoadTaskAsync(id);
        stored!.Status.Should().Be(DomainTaskStatus.Todo, "the stale write was rejected before any mutation");
    }

    [Fact]
    public async Task Deny_out_of_enum_status_is_rejected_422_validation_failed()
    {
        var owner = await CreateUserAsync("google-sub-board-422", "board422@example.com", "Ada Owner");
        var (id, version) = await SeedTaskWithStatusAsync(owner, "Bad status", DomainTaskStatus.Todo);

        using var response = await SendAsync(
            HttpMethod.Patch, StatusPath(id), TokenFor(owner), new { status = "doing", version });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "the widened validator still closes the enum (D3)");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");

        var stored = await LoadTaskAsync(id);
        stored!.Status.Should().Be(DomainTaskStatus.Todo);
    }
}
