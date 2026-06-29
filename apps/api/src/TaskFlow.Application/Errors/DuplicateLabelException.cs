namespace TaskFlow.Application.Errors;

/// <summary>
/// INTERNAL control-flow signal raised by the label persistence seam when a write loses a concurrency race
/// against a DB constraint: a unique violation on the label graph (the <c>id</c> PK or the
/// <c>(owner_id, name_normalized)</c> unique index, from <c>ILabelRepository.SaveChangesAsync</c>) or a
/// foreign-key violation when applying a concurrently-deleted label (from
/// <c>ITaskLabelRepository.SetForOwnerAsync</c>). It exists so the Application layer can detect the race
/// WITHOUT depending on EF Core / Npgsql types directly (the repository abstractions keep persistence
/// technology out of handlers — mirrors <see cref="DuplicateTaskIdException"/>).
/// </summary>
/// <remarks>
/// This is NOT a public API error: the label handlers ALWAYS catch it and re-resolve/translate, so it never
/// reaches <c>ProblemDetailsMiddleware</c> and has NO HTTP/errorCode mapping. <c>CreateLabel</c> re-resolves
/// (own live row → idempotent 200; else a name/id collision → 422); <c>UpdateLabel</c>/<c>SetTaskLabels</c>
/// translate it to a recoverable 422.
/// </remarks>
public sealed class DuplicateLabelException : Exception
{
    public DuplicateLabelException()
        : base("A label persistence conflict occurred.")
    {
    }

    public DuplicateLabelException(string message)
        : base(message)
    {
    }

    public DuplicateLabelException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
