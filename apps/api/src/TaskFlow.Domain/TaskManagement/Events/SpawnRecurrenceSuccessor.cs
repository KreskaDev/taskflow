using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.TaskManagement.Events;

/// <summary>
/// Scheduled message (slice 012, FR-009 / EC-05, research D4/D7): published through the Wolverine
/// transactional outbox when a recurring <see cref="Task"/> is completed BEFORE its due day, with
/// <c>ScheduledTime</c> = the start of that due day in Europe/Warsaw. Picked off the durable
/// <c>recurrence-spawn</c> local queue, its handler re-checks state (still done at the same
/// <see cref="CompletedAtInstant"/>, rule + due still present, no live successor) and spawns the
/// successor exactly once — or no-ops.
/// </summary>
/// <param name="TaskId">The completed instance.</param>
/// <param name="CompletedAtInstant">The <c>completed_at</c> stamped by the completion that scheduled this message.</param>
public sealed record SpawnRecurrenceSuccessor(TaskId TaskId, DateTime CompletedAtInstant) : DomainEvent;
