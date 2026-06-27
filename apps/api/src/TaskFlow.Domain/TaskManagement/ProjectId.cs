namespace TaskFlow.Domain.TaskManagement;

/// <summary>
/// Strongly-typed identifier for <see cref="Project"/> (ENT-02). Backed by a UUIDv7
/// (time-ordered) GUID, client-generated per data-model.md (FR-001 parity).
/// </summary>
/// <remarks>
/// Mirrors <see cref="TaskId"/>: the server never calls a <c>New()</c> factory — the id
/// arrives in the create payload and the row is mapped <c>ValueGeneratedNever()</c>.
/// </remarks>
public readonly record struct ProjectId(Guid Value)
{
    /// <summary>Wraps an existing GUID (e.g. read from the database or the create payload).</summary>
    public static ProjectId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
