using TaskFlow.Domain.TaskManagement;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Persistence seam for the <see cref="Comment"/> aggregate (ENT-08, data-model.md §6). Defined in the
/// Application layer (implemented in Infrastructure over EF Core) so handlers never depend on the persistence
/// technology directly. Loaded/mutated one-aggregate-per-transaction (ADR-0003 — R1/R5/R12). The mention set
/// is loaded with the aggregate.
/// </summary>
/// <remarks>
/// <see cref="FindByIdAsync"/> and <see cref="ListByTaskAsync"/> are <b>live-only</b> (they filter
/// <c>deleted_at IS NULL</c>, so a soft-deleted comment leaves the thread — R5).
/// <see cref="FindByIdIncludingDeletedAsync"/> is <b>tombstone-inclusive</b>, for <c>DeleteComment</c>'s
/// idempotent replay and the <c>ReapDeletedComment</c> reaper (mirroring
/// <c>ITaskRepository.FindByIdIncludingDeletedAsync</c>).
/// </remarks>
public interface ICommentRepository
{
    /// <summary>Stages a new comment (with its mention set) for insertion.</summary>
    Task AddAsync(Comment comment, CancellationToken cancellationToken);

    /// <summary>Finds a <b>live</b> comment (<c>deleted_at IS NULL</c>) with its mention set, or null.</summary>
    Task<Comment?> FindByIdAsync(CommentId id, CancellationToken cancellationToken);

    /// <summary>Finds a comment <b>tombstone-inclusive</b> (soft-deleted rows too) — for the delete replay + the reaper.</summary>
    Task<Comment?> FindByIdIncludingDeletedAsync(CommentId id, CancellationToken cancellationToken);

    /// <summary>Lists a task's <b>live</b> thread chronologically by <c>created_at</c> (filters <c>deleted_at IS NULL</c>).</summary>
    Task<IReadOnlyList<Comment>> ListByTaskAsync(TaskId taskId, CancellationToken cancellationToken);

    /// <summary>Stages a comment for hard deletion (the reaper's purge).</summary>
    void Remove(Comment comment);

    /// <summary>Commits staged changes to the database.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
