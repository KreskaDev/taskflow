namespace TaskFlow.Domain.TaskManagement;

/// <summary>
/// Strongly-typed identifier for <see cref="Cycle"/> (slice 011, D1). Backed by a UUIDv7
/// (time-ordered) GUID, client-generated per the idempotent-PUT convention (FR-001 parity).
/// </summary>
/// <remarks>
/// Only the NON-nullable PK uses this type. The task-side FK (<c>Task.CycleId</c>) deliberately
/// stays a raw <c>Guid?</c> (D2 — the slice-005 value-converted-nullable-FK translation trap).
/// </remarks>
public readonly record struct CycleId(Guid Value)
{
    /// <summary>Wraps an existing GUID (e.g. read from the database or the create payload).</summary>
    public static CycleId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
