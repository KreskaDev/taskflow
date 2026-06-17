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
