using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.TaskManagement;
using TaskFlow.Domain.TaskManagement;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ICommentRepository"/> over <see cref="AppDbContext"/>. The context is
/// the Wolverine-integrated scoped DbContext, so writes participate in the per-message transaction/outbox
/// (the <c>UserMentioned</c> / <c>ReapDeletedComment</c> publishes commit atomically with the row). Comments
/// are <b>versionless</b> (LWW, R2) — no <see cref="DbUpdateConcurrencyException"/> translation. The owned
/// <c>comment_mentions</c> collection is eager-loaded automatically with the aggregate (EF owned-type
/// semantics), so the finds need no explicit Include.
/// </summary>
public sealed class CommentRepository(AppDbContext db) : ICommentRepository
{
    public Task AddAsync(Comment comment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(comment);
        db.Comments.Add(comment);
        return Task.CompletedTask;
    }

    // Live-only (deleted_at IS NULL): a soft-deleted comment leaves the thread + edit/read surface (R5).
    public Task<Comment?> FindByIdAsync(CommentId id, CancellationToken cancellationToken) =>
        db.Comments.FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null, cancellationToken);

    // Tombstone-inclusive: for DeleteComment's idempotent replay (own already-deleted → 204) + the reaper (R5).
    public Task<Comment?> FindByIdIncludingDeletedAsync(CommentId id, CancellationToken cancellationToken) =>
        db.Comments.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    // The chronological live thread (deleted_at IS NULL) — the ix_comments_task_created index path (R5).
    public async Task<IReadOnlyList<Comment>> ListByTaskAsync(TaskId taskId, CancellationToken cancellationToken) =>
        await db.Comments
            .Where(c => c.TaskId == taskId && c.DeletedAt == null)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Remove(Comment comment)
    {
        ArgumentNullException.ThrowIfNull(comment);
        db.Comments.Remove(comment);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);
}
