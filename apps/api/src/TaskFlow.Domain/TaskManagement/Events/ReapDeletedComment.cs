using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.TaskManagement.Events;

/// <summary>
/// Scheduled reaper message: published through the Wolverine transactional outbox when a
/// <see cref="Comment"/> is soft-deleted, with a delay before it is picked off the durable
/// <c>comment-reaper</c> local queue to hard-delete the row (slice 009, R5 — the
/// <see cref="ReapDeletedTask"/> mirror giving the Constitution VII 30s-undo window).
/// </summary>
/// <remarks>
/// Carries the soft-deleted <see cref="CommentId"/> and the <see cref="DeletedAtInstant"/> the row was
/// marked deleted, so the handler can confirm the row is still the same tombstone (not restored by a
/// slice-014 undo, nor re-deleted at a new instant) before erasing it. Because comments are versionless
/// there is no <c>Version</c> backstop — the instant match is the sole guard.
/// </remarks>
public sealed record ReapDeletedComment(CommentId CommentId, DateTime DeletedAtInstant) : DomainEvent;
