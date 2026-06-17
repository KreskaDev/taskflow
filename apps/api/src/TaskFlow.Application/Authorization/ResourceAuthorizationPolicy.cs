using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Application.Authorization;

/// <summary>
/// Deny-by-default implementation of <see cref="IResourceAuthorizationPolicy"/>.
/// In slice 001 only the ownership branch exists: access is granted iff the
/// caller is authenticated AND is the resource owner. Queries that use this
/// policy are scoped to the caller. The shared-project branch
/// (membership + role) is added in slice 007.
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
}
