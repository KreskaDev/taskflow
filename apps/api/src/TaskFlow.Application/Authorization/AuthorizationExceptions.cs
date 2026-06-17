namespace TaskFlow.Application.Authorization;

/// <summary>
/// Thrown when a request reaches a handler without a valid authenticated principal.
/// Mapped to HTTP 401 with <c>errorCode = "unauthenticated"</c> (ADR-0009).
/// </summary>
public sealed class UnauthenticatedException : Exception
{
    public UnauthenticatedException()
        : base("Authentication is required to perform this operation.")
    {
    }

    public UnauthenticatedException(string message)
        : base(message)
    {
    }

    public UnauthenticatedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when an authenticated caller is denied access to a resource they do
/// not own (or lack the membership/role for). Mapped to HTTP 403 with
/// <c>errorCode = "forbidden"</c> (ADR-0009).
/// </summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException()
        : base("You do not have access to this resource.")
    {
    }

    public ForbiddenException(string message)
        : base(message)
    {
    }

    public ForbiddenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
