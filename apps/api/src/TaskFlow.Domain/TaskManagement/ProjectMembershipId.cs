namespace TaskFlow.Domain.TaskManagement;

/// <summary>
/// Strongly-typed identifier for <see cref="ProjectMembership"/> (ENT-07). Backed by a UUIDv7
/// (time-ordered) GUID, client/application-generated per data-model.md §1.
/// </summary>
/// <remarks>
/// Mirrors <see cref="ProjectId"/>/<see cref="TaskId"/>: a surrogate id mapped
/// <c>ValueGeneratedNever()</c>. The membership row is created server-side by the invite/transfer
/// handlers, so the id is minted with <see cref="New"/> rather than arriving on the wire.
/// </remarks>
public readonly record struct ProjectMembershipId(Guid Value)
{
    /// <summary>Generates a new time-ordered (UUIDv7) identity.</summary>
    public static ProjectMembershipId New() => new(Guid.CreateVersion7());

    /// <summary>Wraps an existing GUID (e.g. read from the database).</summary>
    public static ProjectMembershipId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
