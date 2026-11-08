using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskFlow.Domain.IdentityAccess;
using Label = TaskFlow.Domain.TaskManagement.Label;
using LabelId = TaskFlow.Domain.TaskManagement.LabelId;
using RecurrenceRule = TaskFlow.Domain.TaskManagement.RecurrenceRule;
using TaskLabel = TaskFlow.Domain.TaskManagement.TaskLabel;
using TaskFlow.Domain.TaskManagement.Events;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;
using Wolverine;
using Wolverine.Tracking;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;
using TaskStatus = TaskFlow.Domain.TaskManagement.TaskStatus;

namespace TaskFlow.IntegrationTests.Recurrence;

/// <summary>
/// A settable clock for the recurrence suites (D15): "today" vs the due day decides spawn-now vs schedule, and
/// the deferred-job facts advance it to the due day before sending the message directly.
/// </summary>
public sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Shared fixture for <c>TaskFlow.IntegrationTests.Recurrence</c> (slice 012, research D14/D15 — its own ci shard):
/// a frozen Warsaw clock, seeding of recurring tasks straight through the aggregate, the status/edit/due HTTP
/// paths, the scheduled-envelope counter (the comments-reaper helper generalized) and a tracked direct send of
/// <see cref="SpawnRecurrenceSuccessor"/>.
/// </summary>
public abstract class RecurrenceTestBase : SharingTestBase
{
    /// <summary>2026-06-10 10:00Z = 12:00 Europe/Warsaw (CEST) — "today" for every fact unless the clock is moved.</summary>
    protected static readonly DateTimeOffset FrozenNow = new(2026, 6, 10, 10, 0, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    protected MutableTimeProvider Clock { get; } = new(FrozenNow);

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<TimeProvider>(Clock);
    }

    /// <summary>A date-only due stored by the Warsaw-midnight convention: 00:00 Europe/Warsaw on (June 10 + offset) as UTC.</summary>
    protected static DateTime DueOnDay(int offsetFromToday) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Unspecified).AddDays(offsetFromToday), Warsaw);

    protected static string StatusPath(Guid id) => $"/api/tasks/{id}/status";

    protected static string EditPath(Guid id) => $"/api/tasks/{id}/edit";

    protected static string DuePath(Guid id) => $"/api/tasks/{id}/due-date";

    protected static string AssigneesPath(Guid id) => $"/api/tasks/{id}/assignees";

    protected static object Rule(string type, int? interval = null, int[]? weekdays = null, int? dayOfMonth = null) =>
        new { type, interval, weekdays, dayOfMonth };

    /// <summary>Seeds a task straight through the aggregate (optionally recurring, projected, pre-completed).</summary>
    protected async Task<Guid> SeedRecurringTaskAsync(
        UserId createdBy,
        string title,
        DateTime? dueDate,
        RecurrenceRule? rule,
        bool? dueHasTime = null,
        Guid? projectId = null,
        string position = "a0",
        string? description = null,
        string? priority = null,
        string status = "backlog")
    {
        var id = Guid.CreateVersion7();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = FrozenNow.UtcDateTime.AddDays(-1);
        var task = TaskEntity.Create(TaskId.From(id), createdBy, title, position, now, dueDate, dueDate is null ? null : dueHasTime ?? false);
        task.EditTask(title, description, priority, dueDate, dueDate is null ? null : dueHasTime ?? false, projectId is { } pid ? ProjectId.From(pid) : null, rule, now);
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
            task.SetStatus(target, now);
        }

        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return id;
    }

    protected Task<HttpResponseMessage> SendStatusAsync(string token, Guid id, string status, int version) =>
        SendAsync(HttpMethod.Patch, StatusPath(id), token, new { status, version });

    protected async Task<TaskBody> SetStatusAsync(string token, Guid id, string status, int version)
    {
        using var response = await SendStatusAsync(token, id, status, version);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException($"the helper sets status {status} but got {(int)response.StatusCode}; body: {body.Replace('{', '(').Replace('}', ')')}");
        }

        return await response.ReadTaskAsync();
    }

    /// <summary>The whole-object edit body for <paramref name="task"/> with the given rule (and optional due override).</summary>
    protected static object EditBody(TaskBody task, object? recurrenceRule, bool clearDue = false)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new
        {
            title = task.Title,
            description = task.Description,
            priority = task.Priority,
            dueDate = clearDue ? null : task.DueDate,
            dueHasTime = clearDue ? null : task.DueHasTime,
            projectId = task.ProjectId,
            recurrenceRule,
            version = task.Version,
        };
    }

    protected async Task<TaskBody> GetTaskBodyAsync(string token, Guid id)
    {
        // The API has no GET /api/tasks/{id}; read the row and project the fields the edit body needs.
        var row = await LoadTaskAsync(id) ?? throw new InvalidOperationException("Seeded task missing.");
        _ = token;
        return new TaskBody(
            row.Id.Value, row.Title, "backlog", row.Position, row.Version, row.CreatedAt, row.UpdatedAt, row.CompletedAt,
            row.DueDate, row.DueHasTime, row.ProjectId?.Value, row.Priority, row.Description);
    }

    protected async Task<TaskEntity?> FindLiveSuccessorAsync(Guid predecessorId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var predecessor = TaskId.From(predecessorId);
        return await db.Tasks.FirstOrDefaultAsync(t => t.SpawnedFromId == predecessor && t.DeletedAt == null);
    }

    protected async Task<IReadOnlyList<TaskEntity>> ListSuccessorsIncludingDeletedAsync(Guid predecessorId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var predecessor = TaskId.From(predecessorId);
        return await db.Tasks.Where(t => t.SpawnedFromId == predecessor).OrderBy(t => t.CreatedAt).ToListAsync();
    }

    protected async Task<IReadOnlyList<Guid>> ListAllLabelIdsAsync(Guid taskId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.TaskLabels.Where(tl => tl.TaskId == TaskId.From(taskId)).Select(tl => tl.LabelId.Value).ToListAsync();
    }

    /// <summary>Creates a label owned by <paramref name="owner"/> and applies it to the task (the per-user relation).</summary>
    protected async Task<Guid> SeedLabelOnTaskAsync(UserId owner, Guid taskId, string name)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var labelId = LabelId.New();
        db.Labels.Add(Label.Create(labelId, owner, name, "blue", DateTime.UtcNow));
        db.TaskLabels.Add(new TaskLabel(TaskId.From(taskId), labelId));
        await db.SaveChangesAsync();
        return labelId.Value;
    }

    protected async Task<TaskBody> SetAssigneesAsync(string token, Guid taskId, int version, params Guid[] assigneeIds)
    {
        using var response = await SendAsync(HttpMethod.Patch, AssigneesPath(taskId), token, new { assigneeIds, version });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the helper assigns current members");
        return await response.ReadTaskAsync();
    }

    /// <summary>
    /// Counts <typeparamref name="TMessage"/> envelopes parked as 'Scheduled' in Wolverine's durable inbox whose body
    /// mentions <paramref name="taskId"/> (a ScheduledTime/ScheduleDelay publish lands there, where an in-process
    /// tracking session cannot observe it).
    /// </summary>
    protected async Task<int> CountScheduledEnvelopesAsync<TMessage>(Guid taskId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM wolverine.wolverine_incoming_envelopes " +
                "WHERE status = 'Scheduled' " +
                "AND message_type = @messageType " +
                "AND encode(body, 'escape') LIKE '%' || @taskId || '%'";

            var messageType = command.CreateParameter();
            messageType.ParameterName = "messageType";
            messageType.Value = typeof(TMessage).FullName;
            command.Parameters.Add(messageType);

            var idParam = command.CreateParameter();
            idParam.ParameterName = "taskId";
            idParam.Value = taskId.ToString();
            command.Parameters.Add(idParam);

            return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    /// <summary>Delivers the deferred spawn NOW (the handler path), tracked to completion.</summary>
    protected async Task SendSpawnAsync(Guid taskId, DateTime completedAtInstant)
    {
        var host = Services.GetRequiredService<IHost>();
        await host.TrackActivity().Timeout(TimeSpan.FromSeconds(15)).ExecuteAndWaitAsync(
            ctx => ctx.SendAsync(new SpawnRecurrenceSuccessor(TaskId.From(taskId), completedAtInstant)));
    }
}
