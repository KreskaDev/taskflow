using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;
using TaskStatus = TaskFlow.Domain.TaskManagement.TaskStatus;

namespace TaskFlow.IntegrationTests.Cycles;

/// <summary>
/// Shared fixture helpers for the slice-011 cycle suites (contracts/cycles-api.md, task-cycle.md):
/// drive the cycle lifecycle through the real HTTP surface, seed cycle-assigned tasks directly via
/// the DbContext (the slice-004 seeding pattern — reserved columns set through the change-tracker
/// entry), and load persisted rows for assertions. New namespace = a dedicated CI shard (D15).
/// </summary>
public abstract class CyclesTestBase : SharingTestBase
{
    protected static string CyclePath(Guid id) => $"/api/cycles/{id}";

    protected static string ActivatePath(Guid id) => $"/api/cycles/{id}/activate";

    protected static string ClosePath(Guid id) => $"/api/cycles/{id}/close";

    protected static string CycleTasksPath(Guid id) => $"/api/cycles/{id}/tasks";

    protected static string TaskCyclePath(Guid id) => $"/api/tasks/{id}/cycle";

    /// <summary>A date-only UTC instant <paramref name="days"/> days after 2026-01-05 (a fixed Monday anchor).</summary>
    protected static DateTime Day(int days) => new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc).AddDays(days);

    /// <summary>Creates a cycle via <c>PUT /api/cycles/{id}</c>; fails the test unless it 200s.</summary>
    protected async Task<CycleBody> CreateCycleAsync(string token, string name, DateTime start, DateTime end, Guid? id = null)
    {
        using var response = await SendAsync(
            HttpMethod.Put, CyclePath(id ?? Guid.CreateVersion7()), token,
            new { name, startDate = start, endDate = end });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the helper creates a valid cycle");
        return await response.ReadCycleAsync();
    }

    /// <summary>Activates a planned cycle; fails the test unless it 200s.</summary>
    protected async Task<CycleBody> ActivateCycleAsync(string token, CycleBody cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        using var response = await SendAsync(HttpMethod.Patch, ActivatePath(cycle.Id), token, new { version = cycle.Version });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the helper activates a planned cycle");
        return await response.ReadCycleAsync();
    }

    /// <summary>
    /// Seeds a task directly with the slice-011 columns: optional cycle assignment, an arbitrary
    /// status (the rollover matrix needs more than done/backlog), and the carried-over flag
    /// (rollover-written in production; seeded here to prove the manual-write clear).
    /// </summary>
    protected async Task<Guid> SeedCycleTaskAsync(
        UserId createdBy,
        string title,
        Guid? cycleId = null,
        string status = "backlog",
        Guid? projectId = null,
        bool carriedOver = false,
        string position = "a0")
    {
        var id = Guid.CreateVersion7();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = TaskEntity.Create(TaskId.From(id), createdBy, title, position, DateTime.UtcNow);
        var target = status switch
        {
            "backlog" => TaskStatus.Backlog,
            "todo" => TaskStatus.Todo,
            "in_progress" => TaskStatus.InProgress,
            "done" => TaskStatus.Done,
            "cancelled" => TaskStatus.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown wire status."),
        };
        if (target != TaskStatus.Backlog)
        {
            task.SetStatus(target, DateTime.UtcNow);
        }

        var entry = db.Entry(task);
        if (projectId is { } pid)
        {
            entry.Property(nameof(TaskEntity.ProjectId)).CurrentValue = ProjectId.From(pid);
        }

        if (cycleId is not null)
        {
            entry.Property(nameof(TaskEntity.CycleId)).CurrentValue = cycleId;
        }

        if (carriedOver)
        {
            entry.Property("CarriedOver").CurrentValue = true;
        }

        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>Soft-deletes a task row directly (metrics must exclude tombstones).</summary>
    protected async Task SoftDeleteTaskRowAsync(Guid id)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.Tasks.FirstAsync(t => t.Id == TaskId.From(id));
        task.SoftDelete(DateTime.UtcNow);
        await db.SaveChangesAsync();
    }

    /// <summary>Reads a task's persisted cycle assignment + carried-over flag (raw columns).</summary>
    protected async Task<(Guid? CycleId, bool CarriedOver)> LoadTaskCycleStateAsync(Guid id)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.Tasks.FirstAsync(t => t.Id == TaskId.From(id));
        var carried = (bool)db.Entry(task).Property("CarriedOver").CurrentValue!;
        return (task.CycleId, carried);
    }

    /// <summary>Archives a project via the real HTTP surface (EC-12 setup); fails the test unless it 200s.</summary>
    protected async Task ArchiveProjectAsync(string token, ProjectBody project)
    {
        ArgumentNullException.ThrowIfNull(project);
        using var response = await SendAsync(
            HttpMethod.Patch, $"/api/projects/{project.Id}/archive", token,
            new { version = project.Version, childDisposition = (string?)null });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the helper archives an owned project");
    }
}
