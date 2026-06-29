using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Sets a task's priority to a closed-set <c>P0</c>–<c>P3</c> token, or clears it with null (the <c>1</c>-
/// <c>4</c> keys, AS-04, contracts/openapi.yaml <c>setTaskPriority</c>, research R2/R4) under the optimistic-
/// concurrency <c>version</c> guard. The caller is resolved from <see cref="ICurrentUser"/> — the wire NEVER
/// supplies an owner.
/// </summary>
/// <remarks>
/// HTTP request bound by <c>PATCH /api/tasks/{id}/priority</c>: <see cref="Id"/> binds from the route, the
/// rest from the body. Authorization is dispatched by the containing project's visibility
/// (<see cref="TaskAccessGuards.LoadWritableTaskAsync"/>): personal → ownership (foreign → 404); shared →
/// <see cref="EffectiveRole.Editor"/> (viewer → 403, non-member → 404).
/// </remarks>
public sealed record SetPriority
{
    /// <summary>The task identity, carried in the route.</summary>
    public required TaskId Id { get; init; }

    /// <summary>The new priority token (<c>P0</c>–<c>P3</c>), or null to clear (R2).</summary>
    public required string? Priority { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token (R4).</summary>
    public required int Version { get; init; }
}

/// <summary>
/// Validates <see cref="SetPriority"/> at the boundary (research R2): <see cref="SetPriority.Priority"/> is
/// null OR a member of the closed set <c>{P0, P1, P2, P3}</c>. Any other value → <c>422 validation_failed</c>
/// via the wired Wolverine FluentValidation + <c>ProblemDetailsMiddleware</c> pipeline (no new error code).
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors slice-002 SetTaskDoneValidator).")]
public sealed class SetPriorityValidator : AbstractValidator<SetPriority>
{
    public SetPriorityValidator()
    {
        RuleFor(x => x.Priority)
            .Must(TaskPriority.IsValid)
            .WithMessage("Priority must be one of: P0, P1, P2, P3 (or null).");
    }
}

/// <summary>
/// Handles <see cref="SetPriority"/> under the optimistic-concurrency <c>version</c> rule (research R4).
/// Authentication is enforced upstream by the deny-by-default middleware; this handler owns the
/// dispatch-by-visibility load + version-compare + apply.
/// </summary>
/// <remarks>
/// Decision path: <see cref="TaskAccessGuards.LoadWritableTaskAsync"/> with
/// <see cref="EffectiveRole.Editor"/> (personal foreign/absent/soft-deleted → 404; shared viewer → 403,
/// non-member → 404); a stale <see cref="SetPriority.Version"/> → 409 BEFORE the apply; then
/// <c>Task.SetPriority</c> (bumps <c>Version</c>) and persist. The interleaved-race backstop is closed at
/// <c>TaskRepository.SaveChangesAsync</c> (→ 409).
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-002 RenameTaskHandler).")]
public static class SetPriorityHandler
{
    public static async Task<TaskResponse> Handle(
        SetPriority command,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        IResourceAuthorizationPolicy authorization,
        Labels.ITaskLabelRepository taskLabels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(taskLabels);

        var task = await TaskAccessGuards
            .LoadWritableTaskAsync(command.Id, EffectiveRole.Editor, currentUser, tasks, projects, members, authorization, cancellationToken)
            .ConfigureAwait(false);

        if (task.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        task.SetPriority(command.Priority, DateTime.UtcNow);
        await tasks.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var labelIds = await taskLabels.ListLabelIdsForTaskAsync(task.Id, currentUser.Id, cancellationToken).ConfigureAwait(false);
        return TaskResponse.From(task, labelIds);
    }
}
