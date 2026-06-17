using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Application.IdentityAccess;

/// <summary>
/// Persistence seam for the <see cref="User"/> aggregate. Defined in the Application layer
/// (and implemented in Infrastructure over EF Core) so handlers never depend on the
/// persistence technology directly (clean-architecture dependency direction).
/// </summary>
public interface IUserRepository
{
    /// <summary>Finds a user by the immutable Google subject id, or <c>null</c> if none exists.</summary>
    Task<User?> FindByGoogleSubjectIdAsync(string googleSubjectId, CancellationToken cancellationToken);

    /// <summary>Finds a user by TaskFlow id, or <c>null</c> if the row no longer exists (e.g. hard-deleted).</summary>
    Task<User?> FindByIdAsync(UserId id, CancellationToken cancellationToken);

    /// <summary>Stages a newly created user for insertion.</summary>
    void Add(User user);

    /// <summary>Stages a user for hard deletion (account erasure).</summary>
    void Remove(User user);

    /// <summary>Commits staged changes to the database.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
