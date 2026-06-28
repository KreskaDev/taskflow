using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Infrastructure.Persistence;
using DomainProject = TaskFlow.Domain.TaskManagement.Project;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;
using ProjectMembership = TaskFlow.Domain.TaskManagement.ProjectMembership;
using ProjectMembershipId = TaskFlow.Domain.TaskManagement.ProjectMembershipId;

namespace TaskFlow.IntegrationTests.Infrastructure;

/// <summary>
/// Shared fixture helpers for the slice-007 sharing/membership integration suites: seed admitted users
/// (A/B/C/X), create &amp; share projects through the real HTTP surface, seed membership rows directly via
/// the DbContext (so a suite under test does not depend on a not-yet-built command — the slice-004
/// task-seeding pattern), and load the persisted aggregate/rows for assertions.
/// </summary>
public abstract class SharingTestBase : IntegrationTestBase
{
    private const string EnsurePath = "/api/users/ensure";

    protected static string TokenFor(UserId user) => TestJwtHelper.Valid(user.Value.ToString());

    /// <summary>Seeds an admitted User via <c>/api/users/ensure</c> and returns their id.</summary>
    protected async Task<UserId> CreateUserAsync(string sub, string email, string displayName)
    {
        var profile = await (await SendAsync(
            HttpMethod.Post, EnsurePath, TestJwtHelper.Valid(sub),
            new { googleSubjectId = sub, email, displayName, avatarUrl = (string?)null }))
            .ReadProfileAsync();
        return UserId.From(profile.Id);
    }

    /// <summary>Creates a personal project owned by the token's caller (PUT). Fails the test if it does not 200.</summary>
    protected async Task<ProjectBody> CreateProjectAsync(string token, string name = "Work", string color = "blue", string icon = "folder", Guid? parentId = null)
    {
        var id = Guid.CreateVersion7();
        using var response = await SendAsync(HttpMethod.Put, $"/api/projects/{id}", token, new { name, color, icon, parentId });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the helper creates a valid project");
        return await response.ReadProjectAsync();
    }

    /// <summary>Shares a personal project through <c>PATCH /share</c> and returns the updated body.</summary>
    protected async Task<ProjectBody> ShareProjectAsync(string ownerToken, ProjectBody project)
    {
        using var response = await SendAsync(HttpMethod.Patch, $"/api/projects/{project.Id}/share", ownerToken, new { version = project.Version });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the helper shares an owned personal project");
        return await response.ReadProjectAsync();
    }

    /// <summary>Seeds a membership row directly (the row-seeding analogue of the slice-004 task seeding).</summary>
    protected async Task SeedMembershipAsync(Guid projectId, UserId userId, string role)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ProjectMemberships.Add(ProjectMembership.Create(
            ProjectMembershipId.New(), ProjectId.From(projectId), userId, role, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    /// <summary>Seeds a task directly under a project owned by <paramref name="owner"/> (the create command is out of this slice).</summary>
    protected async Task<Guid> SeedTaskUnderProjectAsync(UserId owner, Guid projectId, string title, string position)
    {
        var id = Guid.CreateVersion7();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = TaskFlow.Domain.TaskManagement.Task.Create(
            TaskFlow.Domain.TaskManagement.TaskId.From(id), owner, title, position, DateTime.UtcNow);
        db.Entry(task).Property(nameof(TaskFlow.Domain.TaskManagement.Task.ProjectId)).CurrentValue = ProjectId.From(projectId);
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>
    /// Seeds a task directly (slice 005) with optional project / due-date pair / priority / done — created by
    /// <paramref name="createdBy"/>, version 0 (a fresh non-done seed; <paramref name="done"/> bumps to 1). The
    /// reserved columns are set via the change-tracker entry so the seed needs no command. Returns the id.
    /// </summary>
    protected async Task<Guid> SeedTaskAsync(
        UserId createdBy,
        string title,
        string position = "a0",
        Guid? projectId = null,
        DateTime? dueDate = null,
        bool? dueHasTime = null,
        string? priority = null,
        bool done = false)
    {
        var id = Guid.CreateVersion7();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = TaskFlow.Domain.TaskManagement.Task.Create(
            TaskFlow.Domain.TaskManagement.TaskId.From(id), createdBy, title, position, DateTime.UtcNow, dueDate, dueHasTime);
        if (done)
        {
            task.MarkDone(DateTime.UtcNow);
        }

        var entry = db.Entry(task);
        if (projectId is { } pid)
        {
            entry.Property(nameof(TaskFlow.Domain.TaskManagement.Task.ProjectId)).CurrentValue = ProjectId.From(pid);
        }

        if (priority is not null)
        {
            entry.Property(nameof(TaskFlow.Domain.TaskManagement.Task.Priority)).CurrentValue = priority;
        }

        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>Loads the persisted task aggregate (query-filters ignored), or null — for post-mutation assertions.</summary>
    protected async Task<TaskFlow.Domain.TaskManagement.Task?> LoadTaskAsync(Guid id)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tasks.FirstOrDefaultAsync(t => t.Id == TaskFlow.Domain.TaskManagement.TaskId.From(id));
    }

    /// <summary>Loads all membership rows of a project (assertions on the persisted set).</summary>
    protected async Task<IReadOnlyList<ProjectMembership>> LoadMembershipsAsync(Guid projectId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ProjectMemberships.Where(m => m.ProjectId == ProjectId.From(projectId)).ToListAsync();
    }

    /// <summary>Loads the persisted project aggregate (query-filters ignored), or null.</summary>
    protected async Task<DomainProject?> LoadProjectAsync(Guid id)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == ProjectId.From(id));
    }
}
