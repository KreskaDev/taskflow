using TaskFlow.Domain.Common;
using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Domain.TaskManagement;

/// <summary>
/// A comment on a <b>shared-project</b> <see cref="Task"/> (ENT-08, slice 009). Its <b>own</b> aggregate
/// root (<c>CommentId</c>, server-minted) — NOT owned by <see cref="Task"/> (R1): a comment has an
/// independent lifecycle (author grant, edit/delete, its own @mention set + event), so its consistency
/// boundary is the single comment. It is <b>versionless / last-write-wins</b> (R2): no concurrency token,
/// no <c>version_conflict</c>. It references its parent <see cref="TaskId"/> and its <see cref="AuthorId"/>
/// <b>by id only</b> (no EF navigation collection on <see cref="Task"/>).
/// </summary>
/// <remarks>
/// <para><see cref="Post"/> mints the id and raises <see cref="Events.UserMentioned"/> for the mention set
/// minus any self-mention; <see cref="Edit"/> whole-replaces body + mention set and raises the ADDED-only
/// mention delta (the <c>Task.SetAssignees</c> posture — an empty delta raises nothing, R7);
/// <see cref="SoftDelete"/> stamps <see cref="DeletedAt"/> (idempotent, NO version bump — the
/// <c>Task.SoftDelete</c> mirror minus the version, R5) and the physical row is hard-purged later by the
/// scheduled <c>ReapDeletedComment</c> reaper (the Constitution VII 30s-undo window).</para>
/// <para><see cref="AuthorId"/> is <b>nullable</b>: it is nulled to a tombstone on the author's account
/// erasure (FK → <c>users(id)</c> ON DELETE SET NULL, Constitution XI); the read model then renders a
/// neutral "Deleted user" and the comment survives where it anchors a thread.</para>
/// </remarks>
public sealed class Comment : AggregateRoot<CommentId>
{
    /// <summary>The maximum comment body length in characters (FR-098). The single authoritative value the
    /// command validator, the EF CHECK backstop, and this last-line-of-defence guard all reference.</summary>
    public const int MaxBodyLength = 4000;

    private readonly List<CommentMention> _mentions = [];

    private Comment()
    {
        // EF Core materialization constructor; non-nullable values are populated from the database.
        Body = null!;
    }

    private Comment(CommentId id, TaskId taskId, UserId author, string body, DateTime utcNow)
    {
        Id = id;
        TaskId = taskId;
        AuthorId = author;
        Body = body;
        CreatedAt = utcNow;
    }

    /// <summary>The parent task. FK → <c>tasks(id)</c> ON DELETE CASCADE.</summary>
    public TaskId TaskId { get; private set; }

    /// <summary>The author (immutable provenance grant); NULL once the author's account is erased (R11).</summary>
    public UserId? AuthorId { get; private set; }

    /// <summary>The comment body — trimmed-non-empty, ≤ <see cref="MaxBodyLength"/> (FR-098).</summary>
    public string Body { get; private set; }

    /// <summary>When the comment was posted (UTC).</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>When the comment was last edited (UTC), or null if never edited.</summary>
    public DateTime? EditedAt { get; private set; }

    /// <summary>Soft-delete tombstone (UTC); the thread read filters <c>deleted_at IS NULL</c> (R5).</summary>
    public DateTime? DeletedAt { get; private set; }

    /// <summary>The typed @mention set (User-id tokens), replaced wholesale by the mutators (R6).</summary>
    public IReadOnlyList<CommentMention> Mentions => _mentions.AsReadOnly();

    /// <summary>
    /// Posts a new comment authored by <paramref name="author"/> on <paramref name="taskId"/>. Mints the
    /// server id, de-duplicates + persists the <paramref name="mentions"/> set (self-mention kept), and
    /// raises <see cref="Events.UserMentioned"/> for the mentioned users MINUS the author (a self-mention
    /// notifies no one, R6/R7). Membership + role authorization and mention-candidacy are enforced UPSTREAM
    /// by the handler; the aggregate records the validated content.
    /// </summary>
    public static Comment Post(
        TaskId taskId, ProjectId projectId, UserId author, string body, IEnumerable<UserId> mentions, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(mentions);

        var comment = new Comment(CommentId.New(), taskId, author, NormalizeBody(body), utcNow);
        var distinct = mentions.Distinct().ToList();
        comment.ReplaceMentions(distinct);
        comment.RecordMentionEvent(distinct, projectId, author);
        return comment;
    }

    /// <summary>
    /// Whole-replaces the body and the <paramref name="mentions"/> set (R6), stamping <see cref="EditedAt"/>
    /// on <b>every</b> edit. Raises <see cref="Events.UserMentioned"/> for the ADDED-only mention delta
    /// (minus the author) — an edit with no newly-added mention raises nothing (R7). Versionless / LWW: no
    /// version bump, no conflict (R2). Author-only authorization is enforced UPSTREAM by the handler.
    /// </summary>
    public void Edit(string body, IEnumerable<UserId> mentions, ProjectId projectId, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(mentions);

        Body = NormalizeBody(body);

        var prior = _mentions.Where(m => m.UserId.HasValue).Select(m => m.UserId!.Value).ToHashSet();
        var desired = mentions.Distinct().ToList();
        var added = desired.Where(u => !prior.Contains(u)).ToList();

        ReplaceMentions(desired);
        EditedAt = utcNow;

        if (AuthorId is { } author)
        {
            RecordMentionEvent(added, projectId, author);
        }
    }

    /// <summary>
    /// Soft-deletes the comment (R5): stamps <see cref="DeletedAt"/> so it leaves the thread immediately
    /// (the read filters <c>deleted_at IS NULL</c>) while the row lingers for the 30s undo window before the
    /// scheduled <c>ReapDeletedComment</c> reaper hard-purges it. Idempotent — a call on an already-tombstoned
    /// comment is a guarded no-op (no re-stamp). Versionless: NO version bump (the <c>Task.SoftDelete</c>
    /// mirror minus the version). The scheduled reaper is published by the handler, not the aggregate.
    /// </summary>
    public void SoftDelete(DateTime utcNow)
    {
        if (DeletedAt is not null)
        {
            return; // idempotent guarded no-op (the DeleteTask posture).
        }

        DeletedAt = utcNow;
    }

    private void ReplaceMentions(IReadOnlyCollection<UserId> userIds)
    {
        _mentions.Clear();
        foreach (var userId in userIds)
        {
            _mentions.Add(CommentMention.Create(Id, userId));
        }
    }

    private void RecordMentionEvent(IEnumerable<UserId> added, ProjectId projectId, UserId actor)
    {
        var notify = added.Where(u => u != actor).ToList();
        if (notify.Count == 0)
        {
            return; // a self-only / empty added-delta notifies no one (R7).
        }

        AddDomainEvent(new Events.UserMentioned(Id, TaskId, projectId, notify, actor));
    }

    private static string NormalizeBody(string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        var trimmed = body.Trim();
        if (trimmed.Length > MaxBodyLength)
        {
            throw new ArgumentException($"Comment body must be at most {MaxBodyLength} characters.", nameof(body));
        }

        return trimmed;
    }
}
