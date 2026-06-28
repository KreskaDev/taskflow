using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.TaskManagement;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The shared deny-by-default preamble for the slice-007 owner-only MANAGE operations (invite /
/// change-role / remove / transfer / unshare). Centralizing it gives the non-author authorization review a
/// single chokepoint to audit the load + role-gate + visibility dispatch (research R8/R9): the member-
/// readable load produces the <b>404</b> for a non-member/foreign project; <see cref="EffectiveRole.Owner"/>
/// gating produces the <b>403</b> for an insufficient-role member; and the shared guard makes the /members
/// surface exist only on a shared project. The caller is always <see cref="ICurrentUser"/>, never the wire.
/// </summary>
internal static class MembershipGuards
{
    /// <summary>
    /// Loads the shared project the caller may MANAGE (owner-only), or throws the deny-shaped error:
    /// non-member/foreign → <see cref="NotFoundException"/> (404); insufficient-role member →
    /// <see cref="ForbiddenException"/> (403); a personal project (no /members surface) → 404.
    /// </summary>
    public static async System.Threading.Tasks.Task<Project> LoadManageableSharedProjectAsync(
        ProjectId id,
        ICurrentUser currentUser,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        IResourceAuthorizationPolicy authorization,
        CancellationToken cancellationToken)
    {
        var project = await projects.FindReadableAsync(id, currentUser.Id, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            throw new NotFoundException();
        }

        var memberships = await members.ListByProjectAsync(id, cancellationToken).ConfigureAwait(false);
        authorization.RequireRole(project, memberships, EffectiveRole.Owner);

        // The membership surface exists only on a shared project; a personal one has no /members resource.
        if (project.Visibility != Project.SharedVisibility)
        {
            throw new NotFoundException();
        }

        return project;
    }

    /// <summary>
    /// Loads a project the caller may MANAGE — the visibility dispatch for the slice-004 manage commands
    /// (delete / edit / archive / unarchive), research R8/R9. The <c>personal</c> arm is the unchanged
    /// slice-004 ownership branch (a non-owner → 404 from the readable load, no membership exists); the
    /// <c>shared</c> arm is owner-only (editor/viewer member → 403, non-member → 404). Unlike
    /// <see cref="LoadManageableSharedProjectAsync"/> this does NOT require the project be shared — these
    /// commands apply to personal projects too. The caller is always <see cref="ICurrentUser"/>.
    /// </summary>
    public static async System.Threading.Tasks.Task<Project> LoadOwnerManagedProjectAsync(
        ProjectId id,
        ICurrentUser currentUser,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        IResourceAuthorizationPolicy authorization,
        CancellationToken cancellationToken)
    {
        var project = await projects.FindReadableAsync(id, currentUser.Id, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            throw new NotFoundException();
        }

        // Empty for a personal project (no rows); on a shared project the role gate yields the 403 for an
        // editor/viewer member. ResolveEffectiveRole derives the owner from ownerId in both arms.
        var memberships = await members.ListByProjectAsync(id, cancellationToken).ConfigureAwait(false);
        authorization.RequireRole(project, memberships, EffectiveRole.Owner);

        return project;
    }
}
