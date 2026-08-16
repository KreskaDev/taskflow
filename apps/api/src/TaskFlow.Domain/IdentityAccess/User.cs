using System.Diagnostics.CodeAnalysis;
using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.IdentityAccess;

/// <summary>
/// The identity anchor for the entire system (ENT-06). Every later entity's
/// <c>createdBy</c>, <c>ownerId</c>, assignee, and membership reference this aggregate.
/// </summary>
/// <remarks>
/// <para><c>GoogleSubjectId</c> is the immutable identity anchor (Google's <c>sub</c> claim).
/// <c>Email</c>, <c>DisplayName</c>, and <c>AvatarUrl</c> are refreshed from the Google
/// profile on every sign-in.</para>
/// <para>There is <b>no soft-delete flag</b>: account deletion hard-deletes the row
/// (spec Clarifications 2026-06-17). A returning sign-in by a previously-deleted identity
/// finds no row and creates a brand-new empty account.</para>
/// </remarks>
public sealed class User : AggregateRoot<UserId>
{
    private User()
    {
        // EF Core materialization constructor. Non-nullable values are populated
        // from the database by EF; the null-forgiving defaults satisfy the compiler.
        GoogleSubjectId = null!;
        Email = null!;
        DisplayName = null!;
    }

    private User(UserId id, string googleSubjectId, string email, string displayName, string? avatarUrl, DateTime utcNow)
    {
        Id = id;
        GoogleSubjectId = googleSubjectId;
        Email = email;
        DisplayName = displayName;
        AvatarUrl = avatarUrl;
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
    }

    /// <summary>Google's <c>sub</c> claim — the immutable identity anchor.</summary>
    public string GoogleSubjectId { get; private set; }

    /// <summary>Email from the Google profile; refreshed on each sign-in.</summary>
    public string Email { get; private set; }

    /// <summary>Display name from the Google profile; refreshed on each sign-in.</summary>
    public string DisplayName { get; private set; }

    /// <summary>Avatar URL from the Google profile; refreshed on each sign-in.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
        Justification = "Stored as a text column (data-model.md) and passed through verbatim from the Google id_token to JSON; never parsed as a URI by the domain.")]
    public string? AvatarUrl { get; private set; }

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>Last-profile-refresh timestamp (UTC).</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// The per-user default cycle duration in days (slice 011, FR-015/D8; default 14 = 2 weeks).
    /// Drives ONLY the create-cycle form's end-date pre-fill; each cycle's actual duration is free.
    /// Range 1..90 (validated at the API boundary; guarded here belt-and-braces).
    /// </summary>
    public int CycleDefaultDurationDays { get; private set; } = 14;

    /// <summary>Creates a new user from a first-time Google sign-in.</summary>
    /// <param name="googleSubjectId">Google's <c>sub</c> claim.</param>
    /// <param name="email">Verified email from the Google profile.</param>
    /// <param name="displayName">Display name from the Google profile.</param>
    /// <param name="avatarUrl">Optional avatar URL from the Google profile.</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "Avatar URL is a Google-provided string stored as text; see AvatarUrl.")]
    public static User Create(string googleSubjectId, string email, string displayName, string? avatarUrl, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(googleSubjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new User(UserId.New(), googleSubjectId, email, displayName, avatarUrl, utcNow);
    }

    /// <summary>
    /// Refreshes the mutable profile fields from the Google profile on a returning sign-in.
    /// <see cref="GoogleSubjectId"/> is immutable and is never touched here.
    /// </summary>
    /// <param name="email">Email from the Google profile.</param>
    /// <param name="displayName">Display name from the Google profile.</param>
    /// <param name="avatarUrl">Optional avatar URL from the Google profile.</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings",
        Justification = "Avatar URL is a Google-provided string stored as text; see AvatarUrl.")]
    public void RefreshProfile(string email, string displayName, string? avatarUrl, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        Email = email;
        DisplayName = displayName;
        AvatarUrl = avatarUrl;
        UpdatedAt = utcNow;
    }

    /// <summary>
    /// Sets the default cycle duration preference (slice 011, FR-015/D8). The 1..90 range is
    /// validated at the API boundary (FluentValidation → 422); this guard is belt-and-braces.
    /// </summary>
    /// <param name="days">The new default duration in days (1..90).</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public void SetCycleDefaultDuration(int days, DateTime utcNow)
    {
        if (days is < 1 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(days), days, "The default cycle duration must be 1..90 days.");
        }

        CycleDefaultDurationDays = days;
        UpdatedAt = utcNow;
    }
}
