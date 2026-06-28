using System.Collections.Generic;
using System.Linq;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;

namespace TaskFlow.Application.Authorization;

/// <summary>
/// Deny-by-default implementation of <see cref="IResourceAuthorizationPolicy"/>. The ownership branch
/// (slice 001/004) authorizes personal/unprojected data: access is granted iff the caller is
/// authenticated AND is the resource owner. The shared-project branch (slice 007) resolves the caller's
/// effective role from the owner anchor ∪ the membership set and requires a sufficient role, dispatching
/// on <see cref="Project.Visibility"/> (research R8/R9).
/// </summary>
public sealed class ResourceAuthorizationPolicy(ICurrentUser currentUser) : IResourceAuthorizationPolicy
{
    public bool IsOwner(UserId ownerId) =>
        currentUser.IsAuthenticated && currentUser.Id == ownerId;

    public void RequireOwnership(UserId ownerId)
    {
        if (!IsOwner(ownerId))
        {
            throw new ForbiddenException();
        }
    }

    public EffectiveRole ResolveEffectiveRole(Project project, IReadOnlyCollection<ProjectMembership> memberships, UserId caller)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(memberships);

        // Owner is derived from the immutable anchor in BOTH branches (no row lookup, R2) — true even on a
        // personal project, where the owner is the only authorized caller.
        if (caller == project.OwnerId)
        {
            return EffectiveRole.Owner;
        }

        // Personal projects have no membership branch: a non-owner is a non-member (R8).
        if (project.Visibility != Project.SharedVisibility)
        {
            return EffectiveRole.None;
        }

        var row = memberships.FirstOrDefault(m => m.UserId == caller);
        return row?.Role switch
        {
            MembershipRoles.Editor => EffectiveRole.Editor,
            MembershipRoles.Viewer => EffectiveRole.Viewer,
            _ => EffectiveRole.None,
        };
    }

    public void RequireRole(Project project, IReadOnlyCollection<ProjectMembership> memberships, EffectiveRole required)
    {
        if (!currentUser.IsAuthenticated)
        {
            throw new UnauthenticatedException();
        }

        var role = ResolveEffectiveRole(project, memberships, currentUser.Id);

        // Non-member → 404 (existence not disclosed across the membership boundary, R9). This subsumes the
        // slice-004 ownership 404 for a personal project (a non-owner resolves to None).
        if (role == EffectiveRole.None)
        {
            throw new NotFoundException();
        }

        // Member with insufficient role → 403 (the member already knows the project exists, R9).
        if (role < required)
        {
            throw new ForbiddenException();
        }
    }
}
