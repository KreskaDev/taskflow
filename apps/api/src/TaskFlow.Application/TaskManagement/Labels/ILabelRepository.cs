using TaskFlow.Domain.IdentityAccess;
using Label = TaskFlow.Domain.TaskManagement.Label;
using LabelId = TaskFlow.Domain.TaskManagement.LabelId;

namespace TaskFlow.Application.TaskManagement.Labels;

/// <summary>
/// Persistence seam for the per-user <see cref="Label"/> aggregate (ENT-04). Defined in the Application
/// layer (implemented in Infrastructure over EF Core) so handlers never depend on the persistence
/// technology directly. All reads are owner-scoped (Tier A, FR-065).
/// </summary>
public interface ILabelRepository
{
    /// <summary>Finds the caller's label by id (owner-scoped), or <c>null</c> — a foreign/absent id resolves to null (the handler maps that to 404).</summary>
    Task<Label?> FindOwnedAsync(LabelId id, UserId owner, CancellationToken cancellationToken);

    /// <summary>Lists the caller's labels (owner-scoped), ordered by name — the roster (FR-065/R6).</summary>
    Task<IReadOnlyList<Label>> ListForOwnerAsync(UserId owner, CancellationToken cancellationToken);

    /// <summary>The set of label ids the caller owns — the cross-row check behind <c>SetTaskLabels</c> (R4).</summary>
    Task<IReadOnlySet<LabelId>> ListIdsForOwnerAsync(UserId owner, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the caller already owns a label whose normalized name equals <paramref name="nameNormalized"/>,
    /// optionally excluding <paramref name="excludingId"/> (the self-exclusion on rename). The handler's
    /// pre-check that yields the friendly 422 ahead of the DB unique-index backstop (R7).
    /// </summary>
    Task<bool> ExistsByNormalizedNameForOwnerAsync(UserId owner, string nameNormalized, LabelId? excludingId, CancellationToken cancellationToken);

    /// <summary>Stages a newly created label for insertion.</summary>
    void Add(Label label);

    /// <summary>Stages a label for HARD deletion (physical row removal; FK cascade clears its applications).</summary>
    void Remove(Label label);

    /// <summary>Commits staged changes to the database.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
