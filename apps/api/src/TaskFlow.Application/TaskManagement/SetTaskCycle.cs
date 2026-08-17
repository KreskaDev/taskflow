using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Application.TaskManagement.Cycles;
using FluentValidation;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Assigns a task to a cycle, or clears the assignment with null (slice 011, US-05.AS-01/02,
/// contracts/task-cycle.md <c>setTaskCycle</c>) under the optimistic-concurrency <c>version</c>
/// guard. The caller is resolved from <see cref="ICurrentUser"/> — the wire never supplies one.
/// </summary>
/// <remarks>
/// HTTP request bound by <c>PATCH /api/tasks/{id}/cycle</c>. Authorization is dispatched by the
/// TASK's visibility (<see cref="TaskAccessGuards.LoadWritableTaskAsync"/> — the SetPriority
/// pattern): personal → ownership (foreign → 404); shared → editor/owner (viewer → 403,
/// non-member → 404). The CYCLE needs no additional authorization (team-wide) but must EXIST
/// (unknown id → 422) — ANY status is assignable, incl. closed (Clarifications 2026-08-16).
/// </remarks>
public sealed record SetTaskCycle
{
    /// <summary>The task identity, carried in the route.</summary>
    public required TaskId Id { get; init; }

    /// <summary>The target cycle, or null = back to the cycle backlog (FR-016).</summary>
    public required Guid? CycleId { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token.</summary>
    public required int Version { get; init; }
}

/// <summary>
/// Handles <see cref="SetTaskCycle"/>: the dispatch-by-visibility load + cycle-existence check
/// (422 — no oracle beyond existence: cycles are team-wide) + version-compare + apply
/// (<c>Task.SetCycle</c> ALWAYS clears <c>carried_over</c>, D7) + persist.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors SetPriorityHandler).")]
public static class SetTaskCycleHandler
{
    public static async Task<TaskResponse> Handle(
        SetTaskCycle command,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        IResourceAuthorizationPolicy authorization,
        ICycleRepository cycles,
        Labels.ITaskLabelRepository taskLabels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(cycles);
        ArgumentNullException.ThrowIfNull(taskLabels);

        var task = await TaskAccessGuards
            .LoadWritableTaskAsync(command.Id, EffectiveRole.Editor, currentUser, tasks, projects, members, authorization, cancellationToken)
            .ConfigureAwait(false);

        if (command.CycleId is { } cycleId
            && !await cycles.ExistsAsync(cycleId, cancellationToken).ConfigureAwait(false))
        {
            throw new ValidationException(
                [new FluentValidation.Results.ValidationFailure("cycleId", "The cycle does not exist.")]);
        }

        if (task.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        task.SetCycle(command.CycleId, DateTime.UtcNow);
        await tasks.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var labelIds = await taskLabels.ListLabelIdsForTaskAsync(task.Id, currentUser.Id, cancellationToken).ConfigureAwait(false);
        return TaskResponse.From(task, labelIds);
    }
}
