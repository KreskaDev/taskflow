namespace TaskFlow.Application.Errors;

/// <summary>
/// INTERNAL control-flow signal raised by the persistence seam (<c>ITaskRepository.SaveChangesAsync</c>)
/// when a task insert violates the primary-key uniqueness on <c>id</c> — i.e. a concurrent double-insert
/// of the same client-generated id (research R2: "the PK is the race backstop"). It exists so the
/// Application layer can detect the race WITHOUT depending on EF Core / Npgsql types directly (the
/// <c>ITaskRepository</c> abstraction keeps persistence technology out of handlers).
/// </summary>
/// <remarks>
/// The <c>CreateTask</c> handler ALWAYS catches it and re-resolves through its find-then-decide path
/// (idempotent replay → 200; own tombstone / foreign id → 404), so it never escapes that path. Since
/// slice 019 it is ALSO the public conflict signal of <c>DuplicateTask</c>: a <c>newTaskId</c> taken by
/// an unrelated row escapes to <c>ProblemDetailsMiddleware</c> as <c>409 duplicate_id</c>
/// (contracts/task-duplicate.md).
/// </remarks>
public sealed class DuplicateTaskIdException : Exception
{
    public DuplicateTaskIdException()
        : base("A task with this id already exists.")
    {
    }

    public DuplicateTaskIdException(string message)
        : base(message)
    {
    }

    public DuplicateTaskIdException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
