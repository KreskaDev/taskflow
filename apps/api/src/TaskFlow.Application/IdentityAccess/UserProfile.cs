using System.Diagnostics.CodeAnalysis;
using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Application.IdentityAccess;

/// <summary>
/// The <c>UserProfile</c> response contract (contracts/openapi.yaml) returned by
/// <c>POST /api/users/ensure</c> and <c>GET /api/users/me</c>. Carries only the fields the
/// BFF/UI needs — never the immutable Google subject id.
/// </summary>
public sealed record UserProfile
{
    public required Guid Id { get; init; }

    public required string Email { get; init; }

    public required string DisplayName { get; init; }

    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
        Justification = "Passed through verbatim from the Google profile as text (data-model.md); never parsed as a URI.")]
    public string? AvatarUrl { get; init; }

    public required DateTime CreatedAt { get; init; }

    /// <summary>Projects a <see cref="User"/> aggregate to its wire profile.</summary>
    public static UserProfile From(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new UserProfile
        {
            Id = user.Id.Value,
            Email = user.Email,
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            CreatedAt = user.CreatedAt,
        };
    }
}
