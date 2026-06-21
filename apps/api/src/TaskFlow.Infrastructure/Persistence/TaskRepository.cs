using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.TaskManagement;
using TaskFlow.Domain.IdentityAccess;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ITaskRepository"/> over <see cref="AppDbContext"/>.
/// The context is the Wolverine-integrated scoped DbContext, so writes participate in the
/// per-message transaction/outbox.
/// </summary>
public sealed class TaskRepository(AppDbContext db) : ITaskRepository
{
    public Task<TaskEntity?> FindOwnedAsync(TaskId id, UserId owner, CancellationToken cancellationToken) =>
        db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.CreatedBy == owner && t.DeletedAt == null, cancellationToken);

    public Task<TaskEntity?> FindOwnedIncludingDeletedAsync(TaskId id, UserId owner, CancellationToken cancellationToken) =>
        db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.CreatedBy == owner, cancellationToken);

    public async Task<IReadOnlyList<TaskEntity>> ListOwnedAsync(UserId owner, CancellationToken cancellationToken) =>
        await db.Tasks
            .Where(t => t.CreatedBy == owner && t.DeletedAt == null)
            .OrderBy(t => t.Position)
            .ThenBy(t => t.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(TaskEntity task)
    {
        ArgumentNullException.ThrowIfNull(task);
        db.Tasks.Add(task);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);
}
