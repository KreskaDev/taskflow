namespace TaskFlow.Application.Errors;

/// <summary>
/// Thrown when a handler cannot locate the requested resource (or the caller is
/// not permitted to observe its existence). Mapped to HTTP 404 with
/// <c>errorCode = "not_found"</c> (ADR-0009).
/// </summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException()
        : base("The requested resource was not found.")
    {
    }

    public NotFoundException(string message)
        : base(message)
    {
    }

    public NotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
