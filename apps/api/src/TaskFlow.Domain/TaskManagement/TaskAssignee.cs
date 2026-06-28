using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Domain.TaskManagement;

/// <summary>
/// A single assignee row of a <see cref="Task"/> (slice 008, ENT-01 `assignees`) — an owned child of the
/// Task aggregate, persisted in the <c>task_assignees</c> join table (data-model §1). It carries only the
/// assigned <see cref="UserId"/>; the composite key <c>(task_id, user_id)</c> (the owner FK + this id)
/// enforces set-uniqueness at the database. No behavior of its own — the Task aggregate's <c>version</c>
/// guards it and <see cref="Task.SetAssignees"/> is the only mutator.
/// </summary>
public sealed class TaskAssignee
{
    /// <summary>The assigned user. A current member of the task's shared project (enforced at the handler).</summary>
    public UserId UserId { get; private set; }

    private TaskAssignee()
    {
        // EF Core materialization constructor.
    }

    /// <summary>Creates an assignee row for <paramref name="userId"/>.</summary>
    public TaskAssignee(UserId userId)
    {
        UserId = userId;
    }
}
