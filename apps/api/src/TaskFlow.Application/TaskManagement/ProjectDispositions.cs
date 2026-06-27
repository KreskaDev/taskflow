using FluentValidation;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The disposition tokens + the shared child-disposition reconciliation (AS-10/R5), used by BOTH
/// <see cref="ArchiveProjectHandler"/> (child disposition only) and <see cref="DeleteProjectHandler"/>
/// (task + child dispositions). Built once so the two verticals stay consistent.
/// </summary>
internal static class ProjectDispositions
{
    /// <summary>The <c>childDisposition</c> tokens (AS-10): how a parent's child projects are handled.</summary>
    public const string CascadeChildren = "cascade";
    public const string OrphanToTop = "orphan_to_top";

    /// <summary>The <c>taskDisposition</c> tokens (FR-014/EC-03): how a project's tasks are handled on delete.</summary>
    public const string CascadeTasks = "cascade";
    public const string MoveToInbox = "move_to_inbox";
    public const string ArchiveWithTasks = "archive_with_tasks";

    /// <summary>Whether <paramref name="value"/> is a valid child-disposition token.</summary>
    public static bool IsValidChildDisposition(string? value) =>
        value is CascadeChildren or OrphanToTop;

    /// <summary>Whether <paramref name="value"/> is a valid task-disposition token.</summary>
    public static bool IsValidTaskDisposition(string? value) =>
        value is CascadeTasks or MoveToInbox or ArchiveWithTasks;

    /// <summary>
    /// Applies the child disposition (AS-10) to <paramref name="parentId"/>'s children, in-transaction
    /// BEFORE the parent's own tombstone/archive (R5). Enforces the cross-row "disposition REQUIRED when the
    /// project has children" rule (→ 422, like the nesting guard — not a stateless validator). When children
    /// exist:
    /// <list type="bullet">
    /// <item><b>null/invalid disposition</b> → <see cref="ValidationException"/> (422 on <c>childDisposition</c>).</item>
    /// <item><b><c>orphan_to_top</c></b> → null each child's <c>parent_id</c> (promote to top-level), leaving
    /// them active (an owner-scoped <c>ExecuteUpdate</c> at the repository seam).</item>
    /// <item><b><c>cascade</c></b> → the children FOLLOW the parent's resolved fate: <paramref name="cascadeArchive"/>
    /// <c>true</c> (the parent is being archived, or delete→archive_with_tasks) archives each child; <c>false</c>
    /// (the parent is being soft-deleted) soft-deletes each child (R5). Domain methods, version-bumping.</item>
    /// </list>
    /// A childless project ignores the disposition (it may be null).
    /// </summary>
    /// <param name="parentId">The parent whose children are being reconciled.</param>
    /// <param name="owner">The caller (owner-scoped).</param>
    /// <param name="childDisposition">The caller-chosen token; null/invalid is a 422 when children exist.</param>
    /// <param name="cascadeArchive">For <c>cascade</c>: true = archive the children (reversible), false = soft-delete them (terminal).</param>
    /// <param name="utcNow">The current UTC time.</param>
    /// <param name="projects">The project repository (children load + the orphan set-update).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task ApplyChildDispositionAsync(
        ProjectId parentId,
        UserId owner,
        string? childDisposition,
        bool cascadeArchive,
        DateTime utcNow,
        IProjectRepository projects,
        CancellationToken cancellationToken)
    {
        // The disposition concerns only ACTIVE (non-archived) children — the ones visible in the tree and
        // counted by the client's prompt. An ARCHIVED child is hidden and follows its own lifecycle: it is
        // left untouched here and re-homed on its own unarchive (R9 promotes it to top-level when its parent
        // is gone) or by the FK ON DELETE SET NULL when the tombstoned parent is reaped. Counting archived
        // children here would 422 on children the user cannot see — an unactionable FR-049 error, since the
        // client's childCount excludes them. (The NESTING check, by contrast, still counts archived children
        // via ListChildrenAsync in EditProject, so a project with an archived child cannot be re-parented —
        // which would surface a grandchild on that child's unarchive.)
        var children = await projects
            .ListChildrenAsync(parentId, owner, cancellationToken)
            .ConfigureAwait(false);
        var activeChildren = children.Where(child => child.ArchivedAt is null).ToList();
        if (activeChildren.Count == 0)
        {
            return;
        }

        if (!IsValidChildDisposition(childDisposition))
        {
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    "childDisposition",
                    "A child disposition is required when the project has child projects: 'cascade' or 'orphan_to_top'."),
            ]);
        }

        if (childDisposition == OrphanToTop)
        {
            await projects.OrphanChildrenAsync(parentId, owner, cancellationToken).ConfigureAwait(false);
            return;
        }

        // cascade: the subtree shares the parent's terminal-vs-reversible fate (R5).
        foreach (var child in activeChildren)
        {
            if (cascadeArchive)
            {
                child.Archive(utcNow);
            }
            else
            {
                child.SoftDelete(utcNow);
            }
        }
    }
}
