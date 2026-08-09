using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// A single comment in a task's thread (contracts/openapi.yaml <c>CommentResponse</c>, data-model.md §5).
/// Author identity is <b>tombstone-safe</b> — <see cref="AuthorId"/> is null and <see cref="AuthorDisplayName"/>
/// renders "Deleted user" once the author's account is erased (R11/R15). Carries <b>no email</b>
/// (Constitution XI). <see cref="CanEdit"/> is a UI convenience (the caller authored it) — <b>never</b> the
/// security boundary; the author-only server gate is authoritative (FR-068).
/// </summary>
public sealed record CommentResponse
{
    /// <summary>The comment's server-minted id.</summary>
    public required Guid Id { get; init; }

    /// <summary>The parent task id.</summary>
    public required Guid TaskId { get; init; }

    /// <summary>The author's User id, or null once the author's account is erased (tombstone, R11).</summary>
    public Guid? AuthorId { get; init; }

    /// <summary>The author's display name (output-encoded on render, FR-099); "Deleted user" when erased.</summary>
    public required string AuthorDisplayName { get; init; }

    /// <summary>The comment body (untrusted content — sanitized at the render boundary, FR-098/099).</summary>
    public required string Body { get; init; }

    /// <summary>The typed @mention set (User-id tokens + resolved display names).</summary>
    public required IReadOnlyList<CommentMentionResponse> Mentions { get; init; }

    /// <summary>When the comment was posted (UTC).</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>When the comment was last edited (UTC), or null if never edited.</summary>
    public DateTime? EditedAt { get; init; }

    /// <summary>UI convenience: true iff the caller is the author (drives the edit/delete affordances only).</summary>
    public required bool CanEdit { get; init; }

    /// <summary>The neutral tombstone rendered for an erased author/mention (R11/R15) — never their email.</summary>
    public const string DeletedUserDisplayName = "Deleted user";

    /// <summary>
    /// Projects a <see cref="Comment"/> for <paramref name="caller"/>, resolving author/mention ids to
    /// display names via <paramref name="displayNames"/>. Tombstone-safe: a null id (erased account) — or an
    /// id whose user row is gone — renders <see cref="DeletedUserDisplayName"/>. <c>CanEdit</c> is the
    /// caller-is-author UI convenience only; the author-only server gate (R4) stays authoritative (FR-068).
    /// </summary>
    public static CommentResponse From(
        Comment comment, UserId caller, IReadOnlyDictionary<UserId, string> displayNames)
    {
        ArgumentNullException.ThrowIfNull(comment);
        ArgumentNullException.ThrowIfNull(displayNames);

        return new CommentResponse
        {
            Id = comment.Id.Value,
            TaskId = comment.TaskId.Value,
            AuthorId = comment.AuthorId?.Value,
            AuthorDisplayName = Resolve(comment.AuthorId, displayNames),
            Body = comment.Body,
            Mentions = comment.Mentions
                .Select(m => new CommentMentionResponse
                {
                    UserId = m.UserId?.Value,
                    DisplayName = Resolve(m.UserId, displayNames),
                })
                .ToList(),
            CreatedAt = comment.CreatedAt,
            EditedAt = comment.EditedAt,
            CanEdit = comment.AuthorId is { } author && author == caller,
        };
    }

    private static string Resolve(UserId? id, IReadOnlyDictionary<UserId, string> displayNames) =>
        id is { } userId && displayNames.TryGetValue(userId, out var name) ? name : DeletedUserDisplayName;
}

/// <summary>
/// A resolved @mention token in a <see cref="CommentResponse"/> (contracts/openapi.yaml <c>CommentMention</c>).
/// <see cref="UserId"/> is null for an erased-mention tombstone (R11); the display name is output-encoded.
/// </summary>
public sealed record CommentMentionResponse
{
    /// <summary>The mentioned member's User id, or null once their account is erased (tombstone, R11).</summary>
    public Guid? UserId { get; init; }

    /// <summary>The mentioned member's display name (output-encoded on render); "Deleted user" when erased.</summary>
    public required string DisplayName { get; init; }
}

/// <summary>
/// A task's chronological live thread (contracts/openapi.yaml <c>CommentListResponse</c>): the parent task
/// id + the comments ordered by <c>createdAt</c> (soft-deleted rows excluded — R5).
/// </summary>
public sealed record CommentListResponse
{
    /// <summary>The parent task the thread hangs on.</summary>
    public required Guid TaskId { get; init; }

    /// <summary>The live thread, ordered chronologically by <c>createdAt</c>.</summary>
    public required IReadOnlyList<CommentResponse> Comments { get; init; }
}
