using TaskFlow.Application.Errors;
using TaskFlow.Domain.IdentityAccess;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Application.TaskManagement.Recurrence;

/// <summary>
/// Assembles and stages the NEXT instance of a completed recurring task (slice 012, FR-008, research D5) —
/// shared by the immediate path (<c>SetTaskStatusHandler</c>, due day reached) and the deferred job
/// (<c>SpawnRecurrenceSuccessorHandler</c>). The Domain builds the row (<see cref="TaskEntity.SpawnSuccessor"/>);
/// this helper adds what needs repositories: the adjacent position, the membership-re-validated assignees
/// (FR-066 — a former member is dropped; <c>TaskAssigned</c> is NOT raised, so no re-notification) and, after
/// the caller has saved the row, every owner's label rows (<see cref="StageLabelsAsync"/>).
/// </summary>
internal static class RecurrenceSuccessor
{
    /// <summary>Builds the successor and stages it in the repository (NOT saved — the caller's transaction saves).</summary>
    public static async Task<TaskEntity> BuildAndAddAsync(
        TaskEntity predecessor,
        ITaskRepository tasks,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(predecessor);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(members);

        var position = await AdjacentPosition.AfterAsync(predecessor, tasks, cancellationToken).ConfigureAwait(false);
        var successor = TaskEntity.SpawnSuccessor(predecessor, TaskId.From(Guid.CreateVersion7()), position, utcNow);

        if (predecessor.ProjectId is { } projectId && predecessor.Assignees.Count > 0)
        {
            // Re-validate the carried assignees against CURRENT membership ∪ the owner anchor (FR-008/FR-066).
            var memberships = await members.ListByProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
            var project = await projects.FindByIdAsync(projectId, cancellationToken).ConfigureAwait(false)
                ?? throw new NotFoundException();
            var valid = new HashSet<UserId>(memberships.Select(m => m.UserId)) { project.OwnerId };

            var carried = predecessor.Assignees
                .Select(a => a.UserId)
                .Where(valid.Contains)
                .ToList();
            if (carried.Count > 0)
            {
                successor.SetAssignees(carried, actor: predecessor.CreatedBy, utcNow);
            }
        }

        // Carry-forward must NOT re-fire assignment notifications: drop the TaskAssigned event the domain
        // method recorded before the dispatch drain sees it.
        successor.ClearDomainEvents();

        tasks.Add(successor);
        return successor;
    }

    /// <summary>Copies every owner's label rows from the predecessor (call AFTER the successor row is saved — FK).</summary>
    public static Task StageLabelsAsync(TaskEntity predecessor, TaskEntity successor, Labels.ITaskLabelRepository taskLabels, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(predecessor);
        ArgumentNullException.ThrowIfNull(successor);
        ArgumentNullException.ThrowIfNull(taskLabels);
        return taskLabels.CopyAllAsync(predecessor.Id, successor.Id, cancellationToken);
    }
}
