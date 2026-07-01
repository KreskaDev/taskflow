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
