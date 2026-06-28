namespace TaskFlow.Domain.TaskManagement;

/// <summary>
/// Thrown by <see cref="Project.EnsureNotLastOwner(Project, IdentityAccess.UserId)"/> when an operation
/// targets the project's sole owner (the immutable <see cref="Project.OwnerId"/>) — leave / remove /
/// demote the owner. A genuine domain invariant ("a project always has exactly one owner", ADR-0003
/// Decision 7), so it lives in the Domain layer alongside the guard that raises it. Mapped at the HTTP
/// boundary to <b>409</b> with <c>errorCode = "last_owner"</c> (research R7/R16 / self-review M1) — a
/// recoverable STATE conflict ("transfer ownership to another member first"), NOT field validation, and
/// distinct from <c>version_conflict</c> by <c>errorCode</c> though they share HTTP 409.
/// </summary>
public sealed class LastOwnerException : Exception
{
    private const string DefaultMessage =
        "The project owner cannot be removed, demoted, or leave while they are the sole owner; transfer ownership to another member first.";

    public LastOwnerException()
        : base(DefaultMessage)
    {
    }

    public LastOwnerException(string message)
        : base(message)
    {
    }

    public LastOwnerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
