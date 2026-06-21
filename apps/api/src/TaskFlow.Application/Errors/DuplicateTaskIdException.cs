namespace TaskFlow.Application.Errors;

/// <summary>
/// INTERNAL control-flow signal raised by the persistence seam (<c>ITaskRepository.SaveChangesAsync</c>)
/// when a task insert violates the primary-key uniqueness on <c>id</c> — i.e. a concurrent double-insert
/// of the same client-generated id (research R2: "the PK is the race backstop"). It exists so the
/// Application layer can detect the race WITHOUT depending on EF Core / Npgsql types directly (the
/// <c>ITaskRepository</c> abstraction keeps persistence technology out of handlers).
/// </summary>
/// <remarks>
/// This is NOT a public API error: the <c>CreateTask</c> handler ALWAYS catches it and re-resolves
/// through its find-then-decide path, so it never reaches <c>ProblemDetailsMiddleware</c> and therefore
/// has NO HTTP/errorCode mapping. The re-resolve collapses the race to an idempotent replay (own live
/// row → 200) or to <c>not_found</c> (own tombstone, or a foreign id holding the PK → 404).
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
