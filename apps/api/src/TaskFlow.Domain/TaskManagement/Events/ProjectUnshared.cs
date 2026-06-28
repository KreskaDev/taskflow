using TaskFlow.Domain.Common;
using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Domain.TaskManagement.Events;

/// <summary>
/// Raised when a shared project is re-personalized (research R3/R13) — the handler removes ALL membership
/// rows in the same transaction, so every former member loses ALL access immediately (R10). The authority
/// signal slices 008 (clear assignments), 016 (evict live subscriptions), and 017 (notify) consume; this
/// slice raises but does not consume it.
/// </summary>
/// <remarks>Pure-ID payload: the now-personal project and its retained owner.</remarks>
public sealed record ProjectUnshared(ProjectId ProjectId, UserId OwnerId) : DomainEvent;
