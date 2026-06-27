using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Persistence seam for the <see cref="Project"/> aggregate (ENT-02). Defined in the Application
/// layer (and implemented in Infrastructure over EF Core) so handlers never depend on the
/// persistence technology directly (clean-architecture dependency direction). Mirrors
/// <see cref="ITaskRepository"/>.
/// </summary>
public interface IProjectRepository
{
    /// <summary>
    /// Finds the caller's NON-deleted project by id (owner-scoped + <c>deleted_at IS NULL</c>), or
    /// <c>null</c> if no such row exists. The generic single-row load for edit/archive/unarchive/get —
    /// a foreign, absent, or soft-deleted id all resolve to <c>null</c> (the handler maps that to 404,
    /// R13). Archived rows ARE returned (archive is a reversible state, not a tombstone — R2).
    /// </summary>
    Task<Project?> FindOwnedAsync(ProjectId id, UserId owner, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the caller's project by id WITHOUT the <c>deleted_at IS NULL</c> filter — owner-scoped but
    /// TOMBSTONE-INCLUSIVE. Used ONLY by CREATE so the handler can distinguish an own already-soft-deleted
    /// row (→ the id is spent → 404) from a foreign/absent id (→ attempt the insert); returns <c>null</c>
    /// only when no row owned by <paramref name="owner"/> exists at all (mirrors the task create path).
    /// </summary>
    Task<Project?> FindOwnedIncludingDeletedAsync(ProjectId id, UserId owner, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the caller's NON-deleted projects (owner-scoped + <c>deleted_at IS NULL</c>) for the
    /// sidebar/archived listing (R8). The two view sets are DISJOINT:
    /// <list type="bullet">
    /// <item><paramref name="includeArchived"/> = <c>false</c> → the ACTIVE set
    /// (<c>AND archived_at IS NULL</c>) — the default sidebar (AS-05: archived hidden).</item>
    /// <item><paramref name="includeArchived"/> = <c>true</c> → the ARCHIVED set
    /// (<c>AND archived_at IS NOT NULL</c>) — the archived disclosure so unarchive (AS-11) is reachable.</item>
    /// </list>
    /// </summary>
    Task<IReadOnlyList<Project>> ListOwnedAsync(UserId owner, bool includeArchived, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the NON-deleted child projects of <paramref name="parentId"/> owned by
    /// <paramref name="owner"/> (owner-scoped + <c>deleted_at IS NULL</c>). Used by the handlers to
    /// resolve the cross-row nesting fact (does this project have children? — R3) and the delete/archive
    /// child dispositions (AS-10/R5). Archived children ARE included (archive is reversible).
    /// </summary>
    Task<IReadOnlyList<Project>> ListChildrenAsync(ProjectId parentId, UserId owner, CancellationToken cancellationToken);

    /// <summary>
    /// Promotes every NON-deleted child of <paramref name="parentId"/> owned by <paramref name="owner"/>
    /// to top-level by nulling its <c>parent_id</c> (the <c>orphan_to_top</c> child disposition, AS-10/R5).
    /// An owner-scoped set update at the persistence seam: the <c>parent_id</c> setter is private (no domain
    /// method nulls it outside the archive side-effect, and adding one would land its unit test outside this
    /// vertical), so the reconciliation is an EF <c>ExecuteUpdate</c> rather than per-aggregate mutation.
    /// Returns the number of children promoted. Runs inside the handler's per-message transaction.
    /// </summary>
    Task<int> OrphanChildrenAsync(ProjectId parentId, UserId owner, CancellationToken cancellationToken);

    /// <summary>
    /// Moves every NON-deleted task of <paramref name="projectId"/> owned by <paramref name="owner"/> to the
    /// Inbox by nulling its <c>project_id</c> (the <c>move_to_inbox</c> task disposition, FR-014/EC-03). An
    /// owner-scoped set update at the persistence seam (the Task <c>project_id</c> setter is private and its
    /// move behavior — <c>MoveToProject</c> — is slice-004 US2, outside this vertical), so the reconciliation
    /// is an EF <c>ExecuteUpdate</c>. Returns the number of tasks moved. Runs inside the handler's transaction.
    /// </summary>
    Task<int> MoveProjectTasksToInboxAsync(ProjectId projectId, UserId owner, CancellationToken cancellationToken);

    /// <summary>Stages a newly created project for insertion.</summary>
    void Add(Project project);

    /// <summary>Stages an existing project for HARD deletion (physical row removal; used by the reaper).</summary>
    void Remove(Project project);

    /// <summary>Commits staged changes to the database.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
