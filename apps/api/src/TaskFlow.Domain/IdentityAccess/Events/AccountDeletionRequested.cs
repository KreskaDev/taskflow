using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.IdentityAccess.Events;

/// <summary>
/// Raised when a user requests irreversible erasure of their own account (FR — account deletion).
/// Published through the Wolverine transactional outbox in the SAME transaction that hard-deletes the
/// <see cref="User"/> row, so the dispatch and the deletion commit or roll back atomically.
/// </summary>
/// <remarks>
/// Carries only the deleted user's <see cref="UserId"/>: later slices reattribute that user's
/// authored content to <see cref="UserId.Tombstone"/> keyed on this id. The Google subject id is
/// intentionally omitted — the row delete frees the Google identity, so a later sign-in with the same
/// identity yields a brand-new empty account (no reattachment).
/// </remarks>
public sealed record AccountDeletionRequested(UserId UserId, DateTime RequestedAtUtc) : DomainEvent;
