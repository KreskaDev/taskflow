using TaskFlow.Application.Authorization;

namespace TaskFlow.Application.IdentityAccess.Queries;

/// <summary>
/// Returns the calling user's own profile (FR, US-11.AS-04). Scoped to the caller: the identity is
/// the carrier <c>sub</c> (a TaskFlow user id minted by the proxy), never a client-supplied id.
/// </summary>
public sealed record GetCurrentUser;

/// <summary>
/// Handles <see cref="GetCurrentUser"/>. Authentication is enforced upstream by the deny-by-default
/// middleware (T019); a well-formed carrier whose <c>sub</c> references a row that no longer exists
/// (a hard-deleted account) is rejected as unauthenticated (SC-013).
/// </summary>
public static class GetCurrentUserHandler
{
    public static async Task<UserProfile> Handle(
        GetCurrentUser query,
        ICurrentUser currentUser,
        IUserRepository users,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(users);

        var user = await users.FindByIdAsync(currentUser.Id, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            // The carrier authenticates, but its subject no longer maps to an account.
            throw new UnauthenticatedException("The authenticated user no longer exists.");
        }

        return UserProfile.From(user);
    }
}
