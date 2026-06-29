namespace TaskFlow.Domain.TaskManagement;

/// <summary>
/// Strongly-typed identifier for <see cref="Label"/> (ENT-04). Backed by a UUIDv7
/// (time-ordered) GUID, client-generated per data-model.md (the selector mints it for an
/// optimistic create + paint &lt;16 ms, SC-003).
/// </summary>
/// <remarks>
/// Mirrors <see cref="TaskId"/>: the id arrives in the create payload (the route of
/// <c>PUT /api/labels/{id}</c>) and the row is mapped <c>ValueGeneratedNever()</c>. The
/// <see cref="New"/> factory exists for tests/seeding; the server never mints an id for a
/// caller request.
/// </remarks>
public readonly record struct LabelId(Guid Value)
{
    /// <summary>Mints a fresh time-ordered identity (tests/seeding only — never on a caller request path).</summary>
    public static LabelId New() => new(Guid.CreateVersion7());

    /// <summary>Wraps an existing GUID (e.g. read from the database or the create payload).</summary>
    public static LabelId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
