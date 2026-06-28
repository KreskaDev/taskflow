using System.Collections.Generic;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;

namespace TaskFlow.Application.Authorization;

/// <summary>
/// Deny-by-default, dispatch-by-visibility authorization policy (ADR-0005,
/// Constitution IX). Authorization is decided by the containing resource's
/// visibility:
/// <list type="bullet">
///   <item>Personal / unprojected data authorizes on <b>ownership</b> — the
///   slice-001/004 branch.</item>
///   <item>Shared-project data authorizes on <c>ProjectMembership</c> + role —
///   added in slice 007 (<see cref="ResolveEffectiveRole"/> / <see cref="RequireRole"/>).</item>
/// </list>
/// </summary>
public interface IResourceAuthorizationPolicy
{
    /// <summary>True when the current authenticated caller owns the resource.</summary>
    bool IsOwner(UserId ownerId);

    /// <summary>
    /// Asserts the current caller owns the resource; throws otherwise.
    /// </summary>
    /// <exception cref="ForbiddenException">The caller is not the owner.</exception>
    void RequireOwnership(UserId ownerId);

    /// <summary>
    /// Resolves <paramref name="caller"/>'s <b>effective role</b> on <paramref name="project"/> (research
    /// R2/R8), dispatching on <see cref="Project.Visibility"/> — NOT a conjunction of tiers:
    /// <list type="bullet">
    ///   <item><c>personal</c> → <see cref="EffectiveRole.Owner"/> iff the caller is the owner, else
    ///   <see cref="EffectiveRole.None"/> (no row lookup).</item>
    ///   <item><c>shared</c> → <see cref="EffectiveRole.Owner"/> for the owner (derived from
    ///   <c>OwnerId</c>, no row), else the caller's <c>editor</c>/<c>viewer</c> row, else
    ///   <see cref="EffectiveRole.None"/>.</item>
    /// </list>
    /// A pure function of its arguments (the caller is passed explicitly for testability; the deny path
    /// <see cref="RequireRole"/> coerces it from <see cref="ICurrentUser"/>).
    /// </summary>
    EffectiveRole ResolveEffectiveRole(Project project, IReadOnlyCollection<ProjectMembership> memberships, UserId caller);

    /// <summary>
    /// Asserts the current caller (from <see cref="ICurrentUser"/>, never the wire) holds at least
    /// <paramref name="required"/> on <paramref name="project"/> (research R8/R9). Deny-shape rule:
    /// <list type="bullet">
    ///   <item><b>Non-member</b> (effective role <see cref="EffectiveRole.None"/>) → <see cref="NotFoundException"/>
    ///   (404 — existence not disclosed across the membership boundary).</item>
    ///   <item><b>Member with insufficient role</b> → <see cref="ForbiddenException"/> (403 — the member
    ///   already knows it exists).</item>
    /// </list>
    /// </summary>
    /// <exception cref="UnauthenticatedException">No authenticated caller.</exception>
    /// <exception cref="NotFoundException">The caller is a non-member (404 posture).</exception>
    /// <exception cref="ForbiddenException">The caller is a member but lacks <paramref name="required"/>.</exception>
    void RequireRole(Project project, IReadOnlyCollection<ProjectMembership> memberships, EffectiveRole required);
}
