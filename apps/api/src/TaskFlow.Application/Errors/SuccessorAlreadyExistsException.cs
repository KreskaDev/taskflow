namespace TaskFlow.Application.Errors;

/// <summary>
/// Raised by the persistence seam when inserting a recurrence successor violates
/// <c>ux_tasks_spawned_from_live</c> — a LIVE successor of the same predecessor already exists (slice 012,
/// research D2/D7). The deferred-spawn job swallows it as an idempotent no-op; the immediate path never
/// reaches it because it checks for a live successor first.
/// </summary>
public sealed class SuccessorAlreadyExistsException : Exception
{
    public SuccessorAlreadyExistsException()
        : base("A live successor of this recurring instance already exists.")
    {
    }

    public SuccessorAlreadyExistsException(string message)
        : base(message)
    {
    }

    public SuccessorAlreadyExistsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
