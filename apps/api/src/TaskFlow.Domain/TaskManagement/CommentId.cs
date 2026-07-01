namespace TaskFlow.Domain.TaskManagement;

/// <summary>
/// Strongly-typed identifier for <see cref="Comment"/> (ENT-08). Backed by a UUIDv7 (time-ordered)
/// GUID, <b>server-minted</b> per data-model.md §1 (R1).
/// </summary>
/// <remarks>
/// Mirrors <see cref="ProjectMembershipId"/>: a surrogate id mapped <c>ValueGeneratedNever()</c>. A
/// comment row is created server-side by the <c>PostComment</c> handler, so the id is minted with
/// <see cref="New"/> rather than arriving on the wire (contrast the client-minted <see cref="TaskId"/>).
/// </remarks>
public readonly record struct CommentId(Guid Value)
{
    /// <summary>Generates a new time-ordered (UUIDv7) identity.</summary>
    public static CommentId New() => new(Guid.CreateVersion7());

    /// <summary>Wraps an existing GUID (e.g. read from the database).</summary>
    public static CommentId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
