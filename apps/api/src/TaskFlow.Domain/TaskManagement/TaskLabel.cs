namespace TaskFlow.Domain.TaskManagement;

/// <summary>
/// The <c>task_labels</c> join row — a per-user application of a <see cref="Label"/> to a
/// <see cref="Task"/> (the many-to-many relation, data-model R2). A pure relation row with no behavior
/// and no surrogate id; the composite key <c>(task_id, label_id)</c> is configured in the mapping.
/// </summary>
/// <remarks>
/// This is a <b>standalone relation</b>, NOT an owned child of the <see cref="Task"/> aggregate: labels are
/// per-user, so applying one is a relation mutation served by its own repository and does not load or touch
/// the Task aggregate (and never bumps <c>Task.Version</c>). Ownership is derived through
/// <see cref="LabelId"/> → <c>labels.owner_id</c>, so the join is partitioned per-user without its own owner
/// column. Both FKs cascade on delete (task hard-delete, label delete, and — transitively — account erasure).
/// </remarks>
public sealed class TaskLabel
{
    private TaskLabel()
    {
        // EF Core materialization constructor.
    }

    /// <summary>Creates an application of <paramref name="labelId"/> to <paramref name="taskId"/>.</summary>
    public TaskLabel(TaskId taskId, LabelId labelId)
    {
        TaskId = taskId;
        LabelId = labelId;
    }

    /// <summary>The task the label is applied to. FK → <c>tasks(id)</c> ON DELETE CASCADE. Part of the composite key.</summary>
    public TaskId TaskId { get; private set; }

    /// <summary>The applied label. FK → <c>labels(id)</c> ON DELETE CASCADE. Part of the composite key.</summary>
    public LabelId LabelId { get; private set; }
}
