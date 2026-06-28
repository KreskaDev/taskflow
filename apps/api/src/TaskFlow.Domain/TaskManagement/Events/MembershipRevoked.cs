using TaskFlow.Domain.Common;
using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Domain.TaskManagement.Events;

/// <summary>
/// Raised when a user's access (or editor capability) on a shared project is revoked (research R5/R10/R13).
/// The complete raiser set is <c>RemoveMember</c>, <c>LeaveProject</c>, AND a <c>ChangeMemberRole</c>
/// <b>demotion</b> (editor → viewer) — a demotion revokes the editor capability (ADR-0003:174). A promotion
/// (viewer → editor) is access-additive and raises no event.
/// </summary>
/// <remarks>
/// Pure-ID payload: the project and the affected user. Slice 008 consumes it to clear that user's now-illegal
/// task assignments; slice 016 to evict their live subscriptions; slice 017 to notify. This slice raises but
/// does not consume it. Unlike the three project-state events, this is NOT carried on the <c>Project</c>
/// aggregate's <c>DomainEvents</c> (a <see cref="ProjectMembership"/> row is an entity, not an aggregate
/// root) — the handler publishes it directly to the outbox.
/// </remarks>
public sealed record MembershipRevoked(ProjectId ProjectId, UserId UserId) : DomainEvent;
