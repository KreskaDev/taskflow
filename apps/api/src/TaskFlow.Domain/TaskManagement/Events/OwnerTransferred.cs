using TaskFlow.Domain.Common;
using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Domain.TaskManagement.Events;

/// <summary>
/// Raised when ownership of a shared project moves to a current member (research R6/R13, FR-094) — the
/// only legal mutation of the otherwise-immutable <c>ownerId</c>. The prior owner is demoted to an
/// <c>editor</c> membership row in the same transaction. The authority signal slices 016/017 consume; this
/// slice raises but does not consume it.
/// </summary>
/// <remarks>Pure-ID payload: the project, the demoted prior owner, and the new owner.</remarks>
public sealed record OwnerTransferred(ProjectId ProjectId, UserId PriorOwnerId, UserId NewOwnerId) : DomainEvent;
