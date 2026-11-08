using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Errors;
using TaskFlow.Application.Time;
using Wolverine;
using TaskStatus = TaskFlow.Domain.TaskManagement.TaskStatus;
using TaskFlow.Domain.TaskManagement.Events;

namespace TaskFlow.Application.TaskManagement.Recurrence;

/// <summary>
/// The deferred recurrence spawn (slice 012, FR-009 / EC-05, research D7): consumes
/// <see cref="SpawnRecurrenceSuccessor"/> off the durable <c>recurrence-spawn</c> queue at the start of the
/// completed instance's due day and generates its successor exactly once — or no-ops. Runs without an
/// HttpContext (exempt from the deny-by-default middleware like the reapers); it acts in the PREDECESSOR's
/// context: the successor inherits the predecessor's owner/project, and the carried assignees are re-validated
/// against CURRENT membership (FR-066) by <see cref="RecurrenceSuccessor"/>.
/// </summary>
/// <remarks>
/// Decision path (contracts/recurrence-api.md §Scheduled message), every branch idempotent:
/// <list type="number">
/// <item>row absent or soft-deleted → no-op;</item>
/// <item>not <c>done</c>, or <c>completed_at</c> differs from the message's instant (µs) → no-op — the instance was
/// reopened/cancelled, or re-completed later (that completion carries its OWN message);</item>
/// <item>rule or due removed meanwhile → no-op; due day moved into the future (the done instance was
/// rescheduled) → re-schedule for the new due day, never spawn early (EC-05);</item>
/// <item>a live successor already exists → no-op;</item>
/// <item>otherwise build + insert; a <see cref="SuccessorAlreadyExistsException"/> (the partial unique index won a
/// race) is swallowed as a no-op.</item>
/// </list>
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors ReapDeletedTaskHandler).")]
public static class SpawnRecurrenceSuccessorHandler
{
    private const long TicksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;

    public static async Task Handle(
        SpawnRecurrenceSuccessor message,
        ITaskRepository tasks,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        Labels.ITaskLabelRepository taskLabels,
        TimeProvider timeProvider,
        IMessageContext messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(taskLabels);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var task = await tasks.FindByIdIncludingDeletedAsync(message.TaskId, cancellationToken).ConfigureAwait(false);
        if (task is null || task.DeletedAt is not null)
        {
            return;
        }

        if (task.Status != TaskStatus.Done || task.CompletedAt is not { } completedAt || !SameInstant(completedAt, message.CompletedAtInstant))
        {
            return;
        }

        if (task.RecurrenceRule is null || task.DueDate is not { } due)
        {
            return;
        }

        // 3b. The anchor may have MOVED since the message was scheduled (a done instance can still be
        //     rescheduled — contracts/recurrence-api.md §RescheduleDueDate). EC-05: never spawn before the
        //     CURRENT due day — re-park the message at the new due day's start instead.
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var dueDay = WarsawDayBounds.WarsawLocalDate(due);
        if (WarsawDayBounds.WarsawLocalDate(now) < dueDay)
        {
            await messages
                .PublishAsync(
                    new SpawnRecurrenceSuccessor(task.Id, message.CompletedAtInstant),
                    new DeliveryOptions { ScheduledTime = new DateTimeOffset(WarsawDayBounds.StartOfDayUtc(dueDay), TimeSpan.Zero) })
                .ConfigureAwait(false);
            return;
        }

        if (await tasks.FindLiveSuccessorAsync(task.Id, cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        var successor = await RecurrenceSuccessor
            .BuildAndAddAsync(task, tasks, projects, members, now, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await tasks.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SuccessorAlreadyExistsException)
        {
            // Race backstop: another spawn committed first — the row we staged was detached by the repository.
            return;
        }

        await RecurrenceSuccessor.StageLabelsAsync(task, successor, taskLabels, cancellationToken).ConfigureAwait(false);
    }

    private static bool SameInstant(DateTime a, DateTime b) =>
        a.Ticks / TicksPerMicrosecond == b.Ticks / TicksPerMicrosecond;
}
