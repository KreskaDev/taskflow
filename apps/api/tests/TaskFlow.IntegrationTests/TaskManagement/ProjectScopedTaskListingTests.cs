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
/// Slice 010 coverage for the D4 scoping repair (contracts/project-tasks.md, FR-066): the
/// project task listing — and its three sibling consumers (sidebar counts, delete-cascade,
/// duplicate-neighbour) — become PROJECT-scoped instead of owner-scoped, so tasks authored
/// by non-owner members stop being invisible to those paths on shared projects.
/// </summary>
/// <remarks>
/// RED-FIRST (Constitution VIII): today <c>TaskRepository.ListByProjectAsync</c> filters by
/// <c>created_by = project.OwnerId</c>, so every member-authored-task assertion below FAILS
/// until the repository + four call sites are repaired (T007/T008). The class name avoids
/// every <c>tasks-core</c> exclusion substring in ci.yml (research D12).
/// </remarks>
public sealed class ProjectScopedTaskListingTests : SharingTestBase
{
    private static string TasksPath(Guid projectId) => $"/api/projects/{projectId}/tasks";

    /// <summary>Seeds a task with an explicit status directly (no command reaches cancelled before this slice).</summary>
    private async Task<Guid> SeedProjectTaskWithStatusAsync(
        UserId createdBy, Guid projectId, string title, DomainTaskStatus status, string position)
    {
        var id = Guid.CreateVersion7();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = TaskEntity.Create(TaskId.From(id), createdBy, title, position, DateTime.UtcNow);
        var entry = db.Entry(task);
        entry.Property(nameof(TaskEntity.ProjectId)).CurrentValue = ProjectId.From(projectId);
        if (status != DomainTaskStatus.Backlog)
        {
            entry.Property(nameof(TaskEntity.Status)).CurrentValue = status;
        }

        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task Listing_returns_member_authored_and_cancelled_tasks_to_owner_and_to_another_member()
    {
        var owner = await CreateUserAsync("google-sub-scope-owner1", "scopeowner1@example.com", "Ada Owner");
        var author = await CreateUserAsync("google-sub-scope-author", "scopeauthor@example.com", "Ed Author");
        var reader = await CreateUserAsync("google-sub-scope-reader", "scopereader@example.com", "Re Reader");
        var project = await CreateProjectAsync(TokenFor(owner), "Scoped");
        await ShareProjectAsync(TokenFor(owner), project);
        await SeedMembershipAsync(project.Id, author, "editor");
        await SeedMembershipAsync(project.Id, reader, "viewer");

        var ownerTaskId = await SeedTaskUnderProjectAsync(owner, project.Id, "Owner's task", "a0");
        var memberTaskId = await SeedTaskUnderProjectAsync(author, project.Id, "Member's task", "a1");
        var cancelledId = await SeedProjectTaskWithStatusAsync(owner, project.Id, "Cancelled task", DomainTaskStatus.Cancelled, "a2");

        // The OWNER sees the member-authored task (FR-066: the project's data, not the owner's slice of it).
        using (var asOwner = await SendAsync(HttpMethod.Get, TasksPath(project.Id), TokenFor(owner)))
        {
            asOwner.StatusCode.Should().Be(HttpStatusCode.OK);
            var rows = await asOwner.ReadTasksAsync();
            rows.Select(t => t.Id).Should().Contain(new[] { ownerTaskId, memberTaskId, cancelledId },
                "the listing is project-scoped: member-authored AND cancelled tasks are all the project's data");
            rows.Select(t => t.Id).Should().ContainInConsecutiveOrder(new[] { ownerTaskId, memberTaskId, cancelledId },
                "ordering is by position (COLLATE \"C\" byte order) across all authors");
        }

        // ANOTHER member (viewer) sees the same full listing — reads are viewer+ (FR-065).
        using (var asReader = await SendAsync(HttpMethod.Get, TasksPath(project.Id), TokenFor(reader)))
        {
            asReader.StatusCode.Should().Be(HttpStatusCode.OK);
            var rows = await asReader.ReadTasksAsync();
            rows.Select(t => t.Id).Should().Contain(new[] { ownerTaskId, memberTaskId, cancelledId });
        }
    }

    [Fact]
    public async Task View_counts_include_member_authored_tasks_in_the_shared_project_count()
    {
        var owner = await CreateUserAsync("google-sub-scope-owner2", "scopeowner2@example.com", "Ada Owner");
        var author = await CreateUserAsync("google-sub-scope-author2", "scopeauthor2@example.com", "Ed Author");
        var project = await CreateProjectAsync(TokenFor(owner), "Counted");
        await ShareProjectAsync(TokenFor(owner), project);
        await SeedMembershipAsync(project.Id, author, "editor");

        await SeedTaskUnderProjectAsync(owner, project.Id, "Owner's task", "a0");
        await SeedTaskUnderProjectAsync(author, project.Id, "Member's task", "a1");

        using var response = await SendAsync(HttpMethod.Get, "/api/views/counts", TokenFor(owner));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var counts = await response.ReadCountsAsync();

        counts.Projects.Should().ContainSingle(p => p.ProjectId == project.Id)
            .Which.Count.Should().Be(2, "the sidebar count matches the (repaired) project listing — member-authored tasks included");
    }

    [Fact]
    public async Task Delete_project_cascade_disposes_member_authored_tasks_leaving_no_orphans()
    {
        var owner = await CreateUserAsync("google-sub-scope-owner3", "scopeowner3@example.com", "Ada Owner");
        var author = await CreateUserAsync("google-sub-scope-author3", "scopeauthor3@example.com", "Ed Author");
        var project = await CreateProjectAsync(TokenFor(owner), "Doomed");
        var shared = await ShareProjectAsync(TokenFor(owner), project);
        await SeedMembershipAsync(project.Id, author, "editor");

        var ownerTaskId = await SeedTaskUnderProjectAsync(owner, project.Id, "Owner's task", "a0");
        var memberTaskId = await SeedTaskUnderProjectAsync(author, project.Id, "Member's task", "a1");

        using var response = await SendAsync(
            HttpMethod.Delete,
            $"/api/projects/{project.Id}?version={shared.Version}&taskDisposition=cascade",
            TokenFor(owner));
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "the owner deletes their project with the cascade disposition");

        var ownerTask = await LoadTaskAsync(ownerTaskId);
        ownerTask!.DeletedAt.Should().NotBeNull("cascade soft-deletes the owner's task");

        var memberTask = await LoadTaskAsync(memberTaskId);
        memberTask!.DeletedAt.Should().NotBeNull(
            "cascade covers MEMBER-authored tasks too — the owner-scoped cascade orphaned them (the D4 integrity bug)");
    }

    [Fact]
    public async Task Duplicate_lands_adjacent_to_its_source_in_the_full_project_ordering()
    {
        // D4 sibling: the duplicate's position neighbour search must see MEMBER-authored rows,
        // else the duplicate can land ON or PAST the member task instead of directly after the source.
        var owner = await CreateUserAsync("google-sub-scope-owner4", "scopeowner4@example.com", "Ada Owner");
        var author = await CreateUserAsync("google-sub-scope-author4", "scopeauthor4@example.com", "Ed Author");
        var project = await CreateProjectAsync(TokenFor(owner), "Ranked");
        await ShareProjectAsync(TokenFor(owner), project);
        await SeedMembershipAsync(project.Id, author, "editor");

        var sourceId = await SeedTaskUnderProjectAsync(owner, project.Id, "Source", "a0");
        await SeedTaskUnderProjectAsync(author, project.Id, "Member successor", "a1");

        var newId = Guid.CreateVersion7();
        using var response = await SendAsync(
            HttpMethod.Post, $"/api/tasks/{sourceId}/duplicate", TokenFor(owner), new { newTaskId = newId });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var duplicate = await response.ReadTaskAsync();

        string.CompareOrdinal(duplicate.Position, "a0").Should().BePositive(
            "the duplicate lands AFTER its source");
        string.CompareOrdinal(duplicate.Position, "a1").Should().BeNegative(
            "the duplicate lands BEFORE the member-authored successor — adjacency over the FULL project ordering (D7/D4)");
    }

    [Fact]
    public async Task Deny_non_member_listing_is_rejected_404_not_found()
    {
        var owner = await CreateUserAsync("google-sub-scope-owner5", "scopeowner5@example.com", "Ada Owner");
        var outsider = await CreateUserAsync("google-sub-scope-outsider", "scopeoutsider@example.com", "Out Sider");
        var project = await CreateProjectAsync(TokenFor(owner), "Private");
        await ShareProjectAsync(TokenFor(owner), project);
        await SeedTaskUnderProjectAsync(owner, project.Id, "Hidden", "a0");

        using var response = await SendAsync(HttpMethod.Get, TasksPath(project.Id), TokenFor(outsider));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "existence is undisclosed to non-members (FR-066/068)");
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("not_found");
    }
}
