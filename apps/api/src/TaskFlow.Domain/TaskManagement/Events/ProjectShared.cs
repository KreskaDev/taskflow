using TaskFlow.Domain.Common;
using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Domain.TaskManagement.Events;

/// <summary>
/// Raised when a personal project is converted to <c>shared</c> (research R3/R13). The authority signal
/// later slices consume (016 live re-auth, 017 notifications); slice 007 raises it but does not consume it.
/// </summary>
/// <remarks>
/// Pure-ID payload: the shared project and its (unchanged) owner. Published through the Wolverine
/// transactional outbox in the same transaction that flips <c>visibility</c>, so dispatch and the write
/// commit or roll back atomically.
/// </remarks>
public sealed record ProjectShared(ProjectId ProjectId, UserId OwnerId) : DomainEvent;
