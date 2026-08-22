using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;

namespace TaskFlow.Application.IdentityAccess.Commands;

/// <summary>
/// Updates the caller's own account preferences (slice 011, FR-015/D8): currently the default
/// cycle duration driving the create-cycle form's end-date pre-fill. Caller-scoped — the identity
/// comes from <see cref="ICurrentUser"/>, never the wire.
/// </summary>
public sealed record SetUserPreferences
{
    /// <summary>The new default cycle duration in days (1..90).</summary>
    public required int CycleDefaultDurationDays { get; init; }
}

/// <summary>Validates <see cref="SetUserPreferences"/> at the boundary: the 1..90 range (D8).</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware.")]
public sealed class SetUserPreferencesValidator : AbstractValidator<SetUserPreferences>
{
    public SetUserPreferencesValidator()
    {
        RuleFor(x => x.CycleDefaultDurationDays)
            .InclusiveBetween(1, 90)
            .WithMessage("The default cycle duration must be between 1 and 90 days.");
    }
}

/// <summary>
/// Handles <see cref="SetUserPreferences"/>: load the caller's own row (a vanished account is
/// unauthenticated, the GetCurrentUser posture) → apply → persist → return the widened profile.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen.")]
public static class SetUserPreferencesHandler
{
    public static async Task<UserProfile> Handle(
        SetUserPreferences command,
        ICurrentUser currentUser,
        IUserRepository users,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(users);

        var user = await users.FindByIdAsync(currentUser.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthenticatedException("The authenticated user no longer exists.");

        user.SetCycleDefaultDuration(command.CycleDefaultDurationDays, DateTime.UtcNow);
        await users.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return UserProfile.From(user);
    }
}
