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
/// <remarks>
/// <para><b>Admission trust boundary</b>: the <i>admission</i> decision (allowlist / Workspace
/// <c>hd</c> + <c>email_verified</c>, FR-087) is made by the BFF before it calls this endpoint; the
/// API does not re-check it. This is sound under the threat model: the API port is internal-only
/// (FR-091) and the BFF↔API carrier is signed with a shared key, so any holder of that key could mint
/// an arbitrary <c>sub</c> regardless. If the API ever becomes reachable beyond the BFF, admission
/// must move (or be duplicated) here.</para>
/// <para><b>Known limitation</b>: the <c>email</c> column is UNIQUE, so two Google identities sharing
/// an email (or a profile email-change colliding with another row) surface as a recoverable sign-in
/// error rather than a merge. Cross-account email reconciliation is out of US1 scope.</para>
/// </remarks>
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
