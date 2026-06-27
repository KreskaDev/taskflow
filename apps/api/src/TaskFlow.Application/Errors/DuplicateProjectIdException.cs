namespace TaskFlow.Application.Errors;

/// <summary>
/// INTERNAL control-flow signal raised by the persistence seam (<c>IProjectRepository.SaveChangesAsync</c>)
/// when a project insert violates the primary-key uniqueness on <c>id</c> — i.e. a concurrent
/// double-insert of the same client-generated id (research R1, mirroring the task PK race backstop).
/// It exists so the Application layer can detect the race WITHOUT depending on EF Core / Npgsql types
/// directly (the <c>IProjectRepository</c> abstraction keeps persistence technology out of handlers).
/// </summary>
/// <remarks>
/// This is NOT a public API error: the <c>CreateProject</c> handler (T013) ALWAYS catches it and
/// re-resolves through its find-then-decide path, so it never reaches <c>ProblemDetailsMiddleware</c>
/// and therefore has NO HTTP/errorCode mapping. The re-resolve collapses the race to an idempotent
/// replay (own live row → 200) or to <c>not_found</c> (own tombstone, or a foreign id holding the
/// PK → 404). Mirrors <see cref="DuplicateTaskIdException"/>.
/// </remarks>
public sealed class DuplicateProjectIdException : Exception
{
    public DuplicateProjectIdException()
        : base("A project with this id already exists.")
    {
    }

    public DuplicateProjectIdException(string message)
        : base(message)
    {
    }

    public DuplicateProjectIdException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
