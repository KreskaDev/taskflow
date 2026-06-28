namespace TaskFlow.Application.Errors;

/// <summary>
/// INTERNAL control-flow signal raised by the persistence seam
/// (<c>IProjectMembershipRepository.SaveChangesAsync</c>) when a membership insert violates the
/// UNIQUE <c>(project_id, user_id)</c> constraint — i.e. a concurrent double-invite of the same user
/// (research R4/R15). It lets the Application layer detect the race WITHOUT depending on EF Core / Npgsql
/// types directly (mirrors <see cref="DuplicateProjectIdException"/>).
/// </summary>
/// <remarks>
/// NOT a public API error: the <c>InviteMember</c> handler pre-checks for an existing member (→ 422) and
/// catches this backstop to re-surface the SAME 422 <c>validation_failed</c> shape, so it never reaches
/// <c>ProblemDetailsMiddleware</c> and has no HTTP/errorCode mapping.
/// </remarks>
public sealed class DuplicateMembershipException : Exception
{
    public DuplicateMembershipException()
        : base("This user is already a member of the project.")
    {
    }

    public DuplicateMembershipException(string message)
        : base(message)
    {
    }

    public DuplicateMembershipException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
