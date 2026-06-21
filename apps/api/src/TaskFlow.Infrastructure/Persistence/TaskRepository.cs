using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaskFlow.Application.Errors;
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

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The only unique constraint on `tasks` is the PK on `id` (no unique on position) —
            // so a 23505 here is a concurrent double-insert of the same client-generated id.
            // DETACH the rejected entity first: it is still tracked in the `Added` state, which would
            // (1) make the handler's re-resolve return this optimistic shadow via EF identity
            // resolution instead of the persisted row, and (2) cause Wolverine's per-message
            // transaction commit (AutoApplyTransactions) to re-attempt the INSERT → an uncaught 500.
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            // Translate the EF/Npgsql-specific exception into an Application-layer signal so the
            // CreateTask handler can re-resolve the race without depending on persistence types
            // (clean-architecture dependency direction; research R2 — "PK is the race backstop").
            throw new DuplicateTaskIdException("A task with this id already exists.", ex);
        }
    }
}
