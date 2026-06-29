using TaskFlow.Domain.IdentityAccess;
using LabelId = TaskFlow.Domain.TaskManagement.LabelId;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Application.TaskManagement.Labels;

/// <summary>
/// Persistence seam for the <c>task_labels</c> relation (the per-user application of labels to tasks,
/// data-model R2). Distinct from the <see cref="Label"/> aggregate repository: this serves the standalone
/// many-to-many relation. Every read/write is <b>caller-scoped</b> — it touches only rows whose label is
/// owned by the caller (per-user isolation), never another member's labels on a shared task.
/// </summary>
public interface ITaskLabelRepository
{
    /// <summary>
    /// Replaces the CALLER's labels on <paramref name="taskId"/> with <paramref name="labelIds"/> — a per-user
    /// whole-set replace (R2). Computes the delta against the caller's CURRENT labels on the task
    /// (<c>task_labels ⋈ labels WHERE owner_id = caller</c>), deletes the removed rows, inserts the added ones,
    /// and commits. <paramref name="labelIds"/> MUST already be validated as owned by <paramref name="owner"/>
    /// (the handler does this). Other owners' rows on the same task are never read or touched. Self-contained
    /// (commits its own changes within the per-message transaction).
    /// </summary>
    Task SetForOwnerAsync(TaskId taskId, UserId owner, IReadOnlyCollection<LabelId> labelIds, CancellationToken cancellationToken);

    /// <summary>The caller's label ids applied to one task (the single-task read projection, R6).</summary>
    Task<IReadOnlyList<Guid>> ListLabelIdsForTaskAsync(TaskId taskId, UserId owner, CancellationToken cancellationToken);

    /// <summary>
    /// The caller's label ids applied to each of <paramref name="taskIds"/> — ONE batched caller-scoped join
    /// (<c>task_labels ⋈ labels WHERE owner_id = caller AND task_id = ANY(@ids)</c>), keyed by task id, for the
    /// list read paths (R6; one query per list, not per row). Tasks with no caller labels are absent from the
    /// map (the caller supplies an empty list for those).
    /// </summary>
    Task<IReadOnlyDictionary<TaskId, IReadOnlyList<Guid>>> ListLabelIdsForTasksAsync(
        IReadOnlyCollection<TaskId> taskIds, UserId owner, CancellationToken cancellationToken);
}
