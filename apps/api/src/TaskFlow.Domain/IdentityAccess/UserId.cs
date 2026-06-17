namespace TaskFlow.Domain.IdentityAccess;

/// <summary>
/// Strongly-typed identifier for <see cref="User"/>. Backed by a UUIDv7
/// (time-ordered) GUID, application-generated per data-model.md.
/// </summary>
/// <remarks>
/// No "reject empty GUID" guard is enforced: the all-zeros GUID is the
/// well-known tombstone identity (<c>00000000-0000-0000-0000-000000000000</c>,
/// "Deleted User") seeded by the initial migration, and must be constructible.
/// </remarks>
public readonly record struct UserId(Guid Value)
{
    /// <summary>The well-known tombstone identity used by erasure-cascade handlers.</summary>
    public static UserId Tombstone { get; } = new(Guid.Empty);

    /// <summary>Generates a new time-ordered (UUIDv7) identity.</summary>
    public static UserId New() => new(Guid.CreateVersion7());

    /// <summary>Wraps an existing GUID (e.g. read from the database or a token claim).</summary>
    public static UserId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
