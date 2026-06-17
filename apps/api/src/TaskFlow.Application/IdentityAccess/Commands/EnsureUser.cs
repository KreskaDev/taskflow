using System.Diagnostics.CodeAnalysis;
using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Application.IdentityAccess.Commands;

/// <summary>
/// Bootstrap command issued by the BFF during the OAuth callback (FR-052): create a new account
/// when the Google subject id is unknown, otherwise match the existing one and refresh its profile.
/// A previously-deleted identity has no row, so it yields a brand-new empty account
/// (spec Clarifications 2026-06-17).
/// </summary>
/// <remarks>
/// This command is the HTTP request body bound by <c>POST /api/users/ensure</c>. The identity is
/// taken from the body (<see cref="GoogleSubjectId"/>), never from <c>currentUser.Id</c>: the carrier
/// presented on this call is authenticated, but its <c>sub</c> is the Google subject id, not a TaskFlow id.
/// </remarks>
public sealed record EnsureUser
{
    public required string GoogleSubjectId { get; init; }

    public required string Email { get; init; }

    public required string DisplayName { get; init; }

    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
        Justification = "Google-provided avatar URL stored/carried as text (data-model.md); never parsed as a URI.")]
    public string? AvatarUrl { get; init; }
}

/// <summary>
/// Handles <see cref="EnsureUser"/>. Authentication is enforced upstream by the deny-by-default
/// authorization middleware (T019); this handler owns the create-or-refresh logic only.
/// </summary>
public static class EnsureUserHandler
{
    public static async Task<UserProfile> Handle(EnsureUser command, IUserRepository users, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(users);

        var now = DateTime.UtcNow;
        var existing = await users.FindByGoogleSubjectIdAsync(command.GoogleSubjectId, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            var created = User.Create(command.GoogleSubjectId, command.Email, command.DisplayName, command.AvatarUrl, now);
            users.Add(created);
            await users.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return UserProfile.From(created);
        }

        existing.RefreshProfile(command.Email, command.DisplayName, command.AvatarUrl, now);
        await users.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return UserProfile.From(existing);
    }
}
