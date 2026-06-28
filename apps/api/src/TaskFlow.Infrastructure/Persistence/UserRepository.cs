using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.IdentityAccess;
using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IUserRepository"/> over <see cref="AppDbContext"/>.
/// The context is the Wolverine-integrated scoped DbContext, so writes participate in the
/// per-message transaction/outbox.
/// </summary>
public sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> FindByGoogleSubjectIdAsync(string googleSubjectId, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(u => u.GoogleSubjectId == googleSubjectId, cancellationToken);

    public Task<User?> FindByIdAsync(UserId id, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        // Case-insensitive exact match (R4), performed in the database (no client enumeration). Npgsql
        // translates EF.Functions.ILike to SQL ILIKE; LIKE metacharacters in the input are escaped (default
        // backslash escape) so the pattern is a literal exact match, not a wildcard.
        var pattern = email.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return db.Users.FirstOrDefaultAsync(u => EF.Functions.ILike(u.Email, pattern), cancellationToken);
    }

    public async Task<IReadOnlyList<User>> ListByIdsAsync(IReadOnlyCollection<UserId> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Users.Where(u => ids.Contains(u.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Add(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        db.Users.Add(user);
    }

    public void Remove(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        db.Users.Remove(user);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);
}
