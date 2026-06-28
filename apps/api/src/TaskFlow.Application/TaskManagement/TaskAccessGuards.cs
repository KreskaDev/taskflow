using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.TaskManagement;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The shared dispatch-by-visibility preamble for the slice-005 task WRITE commands (set-priority,
/// reschedule, edit, toggle-done). Centralizing it gives the non-author authorization review a single
/// chokepoint to audit the task load + visibility dispatch + role gate (research R6/R10; mirrors the
/// slice-007 <see cref="MembershipGuards"/>). The caller is always <see cref="ICurrentUser"/>, never the wire.
/// </summary>
/// <remarks>
/// The personal/Inbox path is <b>provably additive</b> — identical to the slice-002/004 ownership posture
/// (foreign/absent/soft-deleted → 404, owner → allow). Only the shared-project arm is new: a member with an
/// insufficient role (a viewer attempting a write) → 403, a non-member → 404 (existence not disclosed across
/// the membership boundary). The version compare and the apply live in the calling handler.
/// </remarks>
internal static class TaskAccessGuards
{
    /// <summary>
    /// Loads the non-deleted task the caller may mutate at <paramref name="requiredRole"/>, or throws the
    /// deny-shaped error. Dispatches on the containing resource's visibility:
    /// <list type="bullet">
    /// <item>no live row → <see cref="NotFoundException"/> (404).</item>
    /// <item>Inbox/unprojected task (<c>ProjectId is null</c>) → personal ownership: a task not created by the
    /// caller → 404.</item>
    /// <item>projected task → load the project as READABLE (foreign/absent, or a shared project the caller is
    /// not a member of → 404), then <see cref="IResourceAuthorizationPolicy.RequireRole"/> with
    /// <paramref name="requiredRole"/> (personal project → caller resolves to Owner from the anchor and passes;
    /// shared project → viewer &lt; Editor → 403).</item>
    /// </list>
    /// </summary>
    public static async System.Threading.Tasks.Task<TaskEntity> LoadWritableTaskAsync(
        TaskId id,
        EffectiveRole requiredRole,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        IResourceAuthorizationPolicy authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(authorization);

        var task = await tasks.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            throw new NotFoundException();
        }

        // Inbox / unprojected → the personal ownership branch (the slice-002/004 posture, unchanged): a
        // foreign id is indistinguishable from absent → 404, never 403 (the id space is not an oracle).
        if (task.ProjectId is not { } projectId)
        {
            if (task.CreatedBy != currentUser.Id)
            {
                throw new NotFoundException();
            }

            return task;
        }

        // Projected → dispatch on the containing project's visibility. The readable load yields the 404 for a
        // foreign/absent project or a shared project the caller is not a member of; RequireRole then yields the
        // 403 for an insufficient-role member. For a personal project the readable load already proved
        // ownership (memberships empty → the owner anchor resolves to Owner), so the gate passes.
        var project = await projects.FindReadableAsync(projectId, currentUser.Id, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            throw new NotFoundException();
        }

        var memberships = await members.ListByProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        authorization.RequireRole(project, memberships, requiredRole);

        return task;
    }
}
