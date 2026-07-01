using TaskFlow.Domain.Common;
using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Domain.TaskManagement.Events;

/// <summary>
/// Raised when a comment @mentions one or more <b>current</b> project members (slice 009, FR-074,
/// research R7). Carries the <b>newly-added</b> mentioned user ids (the delta on an edit; the full set
/// on a first post, minus any self-mention) + the author/actor. Raised by <see cref="Comment.Post"/> /
/// <see cref="Comment.Edit"/> ONLY when the added-mention delta is non-empty, so the downstream consumer
/// gets "at most one notification per genuine mention."
/// </summary>
/// <remarks>
/// Pure-ID payload (no names/PII — Constitution XI). Published through the Wolverine transactional outbox
/// in the same transaction that persists the comment (the <see cref="TaskAssigned"/> posture). Slice 017
/// (notifications) consumes it to notify each genuinely-added mentioned member, suppressing the actor's own
/// self-mention via <see cref="ActorUserId"/>; slice 009 RAISES it with a no-op handler (so the publish is
/// routable) but delivers no notifications.
/// </remarks>
public sealed record UserMentioned(
    CommentId CommentId,
    TaskId TaskId,
    ProjectId ProjectId,
    IReadOnlyCollection<UserId> MentionedUserIds,
    UserId ActorUserId) : DomainEvent;
