using TaskFlow.Application.Authorization;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.IdentityAccess.Events;
using Wolverine;

namespace TaskFlow.Application.IdentityAccess.Commands;

/// <summary>
/// Irreversibly erases the calling user's own account (FR — account deletion). Carries NO id field:
/// the identity is the carrier <c>sub</c> via <see cref="ICurrentUser"/>, so "delete another user" is
/// impossible by construction — a caller can only ever name itself.
/// </summary>
public sealed record DeleteAccount;

/// <summary>
/// Handles <see cref="DeleteAccount"/>. Authentication is enforced upstream by the deny-by-default
/// middleware (T019). Within the per-message transaction (AutoApplyTransactions +
/// UseEntityFrameworkCoreTransactions + Postgres outbox), this publishes
/// <see cref="AccountDeletionRequested"/> to the outbox and then HARD-deletes the <see cref="User"/>
/// row, so the dispatch and the deletion commit or roll back atomically.
/// </summary>
/// <remarks>
/// An already-deleted or tombstone subject is rejected 401 (NOT idempotent 204), consistent with the
/// deny tests and with <c>GetCurrentUser</c>: a carrier whose <c>sub</c> maps to no live account is not
/// a legitimately authenticated user.
/// </remarks>
public static class DeleteAccountHandler
{
    public static async Task Handle(
        DeleteAccount command,
        ICurrentUser currentUser,
        IUserRepository users,
        IMessageContext messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(messages);

        // The all-zeros tombstone identity is a seeded anonymization sentinel, never a real account.
        if (currentUser.Id == UserId.Tombstone)
        {
            throw new UnauthenticatedException("The authenticated subject is not a valid user.");
        }

        var user = await users.FindByIdAsync(currentUser.Id, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            // The carrier authenticates, but its subject no longer maps to an account.
            throw new UnauthenticatedException("The authenticated user no longer exists.");
        }

        // Capture the id into the event BEFORE Remove, then enqueue on the outbox and delete the row
        // in the SAME transaction (publish-to-outbox + EF DELETE commit/roll back together).
        await messages.PublishAsync(new AccountDeletionRequested(user.Id, DateTime.UtcNow)).ConfigureAwait(false);
        users.Remove(user);
        await users.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
