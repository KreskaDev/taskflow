using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;
using Label = TaskFlow.Domain.TaskManagement.Label;
using LabelId = TaskFlow.Domain.TaskManagement.LabelId;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;
using TaskStatus = TaskFlow.Domain.TaskManagement.TaskStatus;

namespace TaskFlow.IntegrationTests.Search;

/// <summary>
/// Shared fixture for <c>TaskFlow.IntegrationTests.Search</c> (slice 013, research D13 — its own ci shard):
/// the search HTTP path, seeding of tasks with descriptions / labels / bulk rows straight through the aggregates.
/// </summary>
public abstract class SearchTestBase : SharingTestBase
{
    protected static string SearchPath(string q) => $"/api/search?q={Uri.EscapeDataString(q)}";

    protected Task<HttpResponseMessage> SendSearchAsync(string token, string q) =>
        SendAsync(HttpMethod.Get, SearchPath(q), token);

    protected async Task<SearchBody> SearchAsync(string token, string q)
    {
        using var response = await SendSearchAsync(token, q);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException($"search '{q}' returned {(int)response.StatusCode}; body: {body.Replace('{', '(').Replace('}', ')')}");
        }

        return await response.ReadSearchAsync();
    }

    /// <summary>Seeds a task with an optional description / project / status straight through the aggregate.</summary>
    protected async Task<Guid> SeedSearchTaskAsync(
        UserId createdBy,
        string title,
        string? description = null,
        Guid? projectId = null,
        string status = "backlog",
        string position = "a0")
    {
        var id = Guid.CreateVersion7();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var task = TaskEntity.Create(TaskId.From(id), createdBy, title, position, now);
        if (description is not null || projectId is not null)
        {
            task.EditTask(title, description, null, null, null, projectId is { } pid ? ProjectId.From(pid) : null, null, now);
        }

        if (status != "backlog")
        {
            task.SetStatus(status == "done" ? TaskStatus.Done : status == "cancelled" ? TaskStatus.Cancelled : TaskStatus.Todo, now);
        }

        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>Bulk-inserts <paramref name="count"/> personal tasks for the SC-009 characterization.</summary>
    protected async Task SeedManyTasksAsync(UserId createdBy, int count, string titlePrefix)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        var now = DateTime.UtcNow;
        for (var i = 0; i < count; i++)
        {
            var task = TaskEntity.Create(TaskId.From(Guid.CreateVersion7()), createdBy, $"{titlePrefix} {i:D5}", $"a{i:D6}", now);
            db.Tasks.Add(task);
        }

        await db.SaveChangesAsync();
    }

    protected async Task<Guid> SeedLabelAsync(UserId owner, string name)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var labelId = LabelId.New();
        db.Labels.Add(Label.Create(labelId, owner, name, "blue", DateTime.UtcNow));
        await db.SaveChangesAsync();
        return labelId.Value;
    }

    protected async Task ArchiveProjectViaApiAsync(string token, ProjectBody project)
    {
        ArgumentNullException.ThrowIfNull(project);
        using var response = await SendAsync(
            HttpMethod.Patch, $"/api/projects/{project.Id}/archive", token,
            new { version = project.Version, childDisposition = (string?)null });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the helper archives an owned project");
    }

    protected async Task<int> CountTasksAsync(UserId owner)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tasks.CountAsync(t => t.CreatedBy == owner);
    }
}
