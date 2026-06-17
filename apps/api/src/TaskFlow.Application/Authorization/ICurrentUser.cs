using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Application.Authorization;

/// <summary>
/// The authenticated principal for the current request, derived from the
/// BFF-minted JWT carrier (the API layer implements this over <c>HttpContext</c>).
/// </summary>
public interface ICurrentUser
{
    /// <summary>True when a valid identity carrier was presented.</summary>
    bool IsAuthenticated { get; }

    /// <summary>The caller's TaskFlow user id (the JWT <c>sub</c> claim).</summary>
    /// <exception cref="System.InvalidOperationException">Thrown when not authenticated.</exception>
    UserId Id { get; }
}
