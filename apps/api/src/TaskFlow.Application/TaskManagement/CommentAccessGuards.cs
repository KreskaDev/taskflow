using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.TaskManagement;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The load-parent-then-authorize preamble for the slice-009 comment commands/query. Mirrors
/// <see cref="TaskAccessGuards"/> but with the <b>personal branch DENIED</b> — comments live ONLY on tasks in
/// <b>shared</b> projects (FR-072), so an Inbox/unprojected task or a personal-visibility project yields a
/// <b>404</b> (no comment surface, no existence leak). Centralizing it gives the non-author authorization
/// review a single chokepoint (R3). The caller is always <see cref="ICurrentUser"/>, never the wire.
/// </summary>
/// <remarks>
/// Dispatch (reusing the slice-007 policy unchanged — no fork):
/// <list type="bullet">
/// <item>no live parent task → 404;</item>
/// <item>Inbox/unprojected task (<c>ProjectId is null</c>) → 404 (personal tasks have no comment surface);</item>
/// <item>project not readable (foreign/absent, or a shared project the caller is not a member of) → 404;</item>
/// <item>readable but <b>personal</b> visibility → 404 (a personal-named project the caller owns still has no
/// comment surface — the key departure from <see cref="TaskAccessGuards"/>);</item>
/// <item>shared project → <see cref="IResourceAuthorizationPolicy.RequireRole"/> at
/// <paramref name="requiredRole"/> (viewer &lt; Editor → 403); a non-member was already 404'd by the readable load.</item>
/// </list>
/// Returns the resolved (task, project, memberships) for the handler's author-equality step and its
/// mention-candidacy check (candidates = current members).
/// </remarks>
internal static class CommentAccessGuards
{
    public static async System.Threading.Tasks.Task<CommentAccessContext> LoadForCommentAsync(
        TaskId taskId,
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

        var task = await tasks.FindByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            throw new NotFoundException();
        }

        // Inbox / unprojected task → no comment surface (comments live only on shared-project tasks, FR-072).
        if (task.ProjectId is not { } projectId)
        {
            throw new NotFoundException();
        }

        // The readable load 404s a foreign/absent project OR a shared project the caller is not a member of.
        var project = await projects.FindReadableAsync(projectId, currentUser.Id, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            throw new NotFoundException();
        }

        // A personal-visibility project (even one the caller owns) has no comment surface — the departure from
        // TaskAccessGuards, which lets the owner through the personal arm. Comments require a SHARED project.
        if (project.Visibility != Project.SharedVisibility)
        {
            throw new NotFoundException();
        }

        var memberships = await members.ListByProjectAsync(projectId, cancellationToken).ConfigureAwait(false);

        // Reuse the slice-007 membership+role gate verbatim (viewer+ read / editor+ write; viewer-write → 403).
        authorization.RequireRole(project, memberships, requiredRole);

        return new CommentAccessContext(task, project, memberships);
    }
}

/// <summary>
/// The resolved authorization context for a comment operation: the parent task, its shared project, and the
/// current membership roster (for the author-equality step + mention-candidacy check).
/// </summary>
internal sealed record CommentAccessContext(
    TaskEntity Task,
    Project Project,
    IReadOnlyList<ProjectMembership> Memberships);
