using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Domain.TaskManagement;

/// <summary>
/// A typed @mention token on a <see cref="Comment"/> (ENT-08, R6): a reference to a mentioned project
/// member <b>by User id</b> (never scraped from free text). Owned by the <see cref="Comment"/> aggregate
/// and replaced <b>wholesale</b> by the comment's mutators.
/// </summary>
/// <remarks>
/// Carries a <b>surrogate</b> id PK (a PK column cannot be <c>SET NULL</c>, R11/M2) so <see cref="UserId"/>
/// can be nulled to a tombstone on the mentioned user's account erasure (the same residual-attribution rule
/// as the comment author — Constitution XI; FK → <c>users(id)</c> ON DELETE SET NULL). Uniqueness on
/// <c>(comment_id, user_id)</c> de-dups the mention set.
/// </remarks>
public sealed class CommentMention
{
    private CommentMention()
    {
        // EF Core materialization constructor; UserId may be NULL (an erased-mention tombstone).
    }

    private CommentMention(Guid id, CommentId commentId, UserId userId)
    {
        Id = id;
        CommentId = commentId;
        UserId = userId;
    }

    /// <summary>Surrogate identity (UUIDv7, <c>ValueGeneratedNever</c>) — NOT the mentioned user.</summary>
    public Guid Id { get; private set; }

    /// <summary>The owning comment. FK → <c>comments(id)</c> ON DELETE CASCADE.</summary>
    public CommentId CommentId { get; private set; }

    /// <summary>The mentioned member. FK → <c>users(id)</c> ON DELETE SET NULL (nullable tombstone, R11).</summary>
    public UserId? UserId { get; private set; }

    /// <summary>Creates a mention token for <paramref name="userId"/> on <paramref name="commentId"/>.</summary>
    public static CommentMention Create(CommentId commentId, UserId userId) =>
        new(Guid.CreateVersion7(), commentId, userId);
}
