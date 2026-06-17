using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Application.Authorization;

/// <summary>
/// Deny-by-default, dispatch-by-visibility authorization policy (ADR-0005,
/// Constitution IX). Authorization is decided by the containing resource's
/// visibility:
/// <list type="bullet">
///   <item>Personal / unprojected data authorizes on <b>ownership</b> — the
///   only branch exercised in slice 001.</item>
///   <item>Shared-project data authorizes on <c>ProjectMembership</c> + role —
///   added in slice 007.</item>
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
}
