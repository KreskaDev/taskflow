using TaskFlow.Domain.IdentityAccess.Events;

namespace TaskFlow.Application.IdentityAccess;

/// <summary>
/// No-op consumer for <see cref="AccountDeletionRequested"/>. In slice 001 the event has no real
/// effect beyond making the publish ROUTABLE (an unrouted Wolverine publish is silently dropped):
/// a registered handler plus the local-queue route in <c>Program.cs</c> give the message a durable
/// outbox-backed destination, which is what the dispatch is asserted on.
/// </summary>
/// <remarks>
/// Later slices replace this with the real erasure-cascade coordinator (reattribute the deleted user's
/// content to <see cref="TaskFlow.Domain.IdentityAccess.UserId.Tombstone"/>). Do NOT do real work here.
/// </remarks>
public static class AccountDeletionRequestedHandler
{
    public static void Handle(AccountDeletionRequested _)
    {
    }
}
