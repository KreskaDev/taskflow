using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.TaskManagement;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Moves the caller's own task to a project, or to the Inbox (the <c>M</c> action, US-08.AS-05,
/// contracts/openapi.yaml <c>moveTaskToProject</c>, research R7) under the optimistic-concurrency
/// <c>version</c> guard. <see cref="ProjectId"/> = null moves the task to the Inbox (FR-021). The caller
/// is resolved from <see cref="ICurrentUser"/> — the wire NEVER supplies an owner.
/// </summary>
/// <remarks>
/// This is the HTTP request bound by <c>PATCH /api/tasks/{id}/project</c>: <see cref="Id"/> binds from the
/// route, the rest from the body. The handler authorizes ownership of BOTH the task and (when non-null)
/// the target project — either failing resolves to 404 (the ownership posture, R13). There is NO
/// command-local validator: a task→project move has nothing to validate beyond <see cref="Version"/>
/// (one-level nesting is a project→project rule, not applicable here; both ownership facts are 404s).
/// </remarks>
public sealed record MoveTaskToProject
{
    /// <summary>The task identity, carried in the route.</summary>
    public required TaskId Id { get; init; }

    /// <summary>The target project, or null for the Inbox (R7).</summary>
    public ProjectId? ProjectId { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token (R4/R7); a stale value → 409.</summary>
    public required int Version { get; init; }
}

/// <summary>
/// Handles <see cref="MoveTaskToProject"/> under the optimistic <c>version</c> rule, checking ownership of
/// BOTH the task and the target project (R7). Authentication is enforced upstream by the deny-by-default
/// middleware.
/// </summary>
/// <remarks>
/// Decision path (mirrors <c>EditProject</c>'s precedence):
/// <list type="bullet">
/// <item>owner-scoped + NON-deleted task load; a foreign/absent/soft-deleted id → 404 (NEVER 403, R13).</item>
/// <item>a stale <see cref="MoveTaskToProject.Version"/> → 409 <c>version_conflict</c>, BEFORE the move so
/// a rejected request leaves the row untouched.</item>
/// <item>when <see cref="MoveTaskToProject.ProjectId"/> is non-null, resolve the target project as OWNED
/// (foreign/absent → 404, no existence leak, R13) so a task can never be filed under another user's
/// project. A null target (Inbox) skips this.</item>
/// <item>otherwise <c>Task.MoveToProject</c> (bumps <c>Version</c> + stamps <c>UpdatedAt</c>) and persist;
/// the interleaved-race backstop is closed at <c>TaskRepository.SaveChangesAsync</c> (→ 409).</item>
/// </list>
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-002 RenameTaskHandler).")]
public static class MoveTaskToProjectHandler
{
    public static async Task<TaskResponse> Handle(
        MoveTaskToProject command,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        IProjectRepository projects,
        Labels.ITaskLabelRepository taskLabels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(taskLabels);

        var owner = currentUser.Id;

        var task = await tasks
            .FindOwnedAsync(command.Id, owner, cancellationToken)
            .ConfigureAwait(false);
        if (task is null)
        {
            throw new NotFoundException();
        }

        if (task.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        // R7: a non-null target must be a project the caller owns — else 404 (existence not disclosed),
        // so a task can never be filed under another user's project. A null target (Inbox) skips this.
        if (command.ProjectId is { } targetProjectId)
        {
            var target = await projects
                .FindOwnedAsync(targetProjectId, owner, cancellationToken)
                .ConfigureAwait(false);
            if (target is null)
            {
                throw new NotFoundException();
            }
        }

        task.MoveToProject(command.ProjectId, DateTime.UtcNow);
        await tasks.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Labels are project-INDEPENDENT (R5): a move does NOT clear them. Re-project the caller's labels.
        var labelIds = await taskLabels.ListLabelIdsForTaskAsync(task.Id, owner, cancellationToken).ConfigureAwait(false);
        return TaskResponse.From(task, labelIds);
    }
}
