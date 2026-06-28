using TaskFlow.Domain.Common;
using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Domain.TaskManagement.Events;

/// <summary>
/// Raised when a shared-project task's assignee set changes (slice 008, FR-070, research R3). Carries the
/// assignee <b>delta</b> (added/removed user ids) + the <c>ActorUserId</c> who made the change. Raised by
/// <see cref="Task.SetAssignees"/> ONLY on a real change (an idempotent no-op set raises nothing), so the
/// downstream consumer gets "at most one notification per genuine change."
/// </summary>
/// <remarks>
/// Pure-ID payload (no names/PII). Published through the Wolverine transactional outbox in the same
/// transaction that persists the assignee change (the slice-007 <c>DomainEventDispatch</c> pattern,
/// generalized to the <see cref="Task"/> aggregate). Slice 017 (notifications) consumes it to notify each
/// genuinely-added assignee, suppressing the actor's own self-assignment via <see cref="ActorUserId"/>;
/// slice 008 RAISES it with a no-op handler (so the publish is routable) but delivers no notifications.
/// </remarks>
public sealed record TaskAssigned(
    TaskId TaskId,
    ProjectId ProjectId,
    IReadOnlyCollection<UserId> AddedAssigneeIds,
    IReadOnlyCollection<UserId> RemovedAssigneeIds,
    UserId ActorUserId) : DomainEvent;
