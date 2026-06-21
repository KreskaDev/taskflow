namespace TaskFlow.Application.Errors;

/// <summary>
/// Thrown when a mutating command carries a stale optimistic-concurrency
/// <c>version</c> that no longer matches the persisted aggregate. Mapped to
/// HTTP 409 with <c>errorCode = "version_conflict"</c> (ADR-0009).
/// </summary>
public sealed class VersionConflictException : Exception
{
    public VersionConflictException()
        : base("The resource has been modified by another request; reload and retry.")
    {
    }

    public VersionConflictException(string message)
        : base(message)
    {
    }

    public VersionConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
