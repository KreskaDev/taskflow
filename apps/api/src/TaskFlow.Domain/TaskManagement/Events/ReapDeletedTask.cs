using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.TaskManagement.Events;

/// <summary>
/// Scheduled reaper message: published through the Wolverine transactional outbox when a
/// <see cref="Task"/> is soft-deleted, with a delay before it is picked off the durable
/// <c>task-reaper</c> local queue to hard-delete the row (FR — task deletion).
/// </summary>
/// <remarks>
/// Carries the soft-deleted <see cref="TaskId"/> and the <see cref="DeletedAtInstant"/> the
/// row was marked deleted, so the handler can confirm the row is still the same tombstone
/// (and was not undeleted/recreated) before erasing it.
/// </remarks>
public sealed record ReapDeletedTask(TaskId TaskId, DateTime DeletedAtInstant) : DomainEvent;
