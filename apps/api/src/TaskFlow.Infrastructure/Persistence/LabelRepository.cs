using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaskFlow.Application.Errors;
using TaskFlow.Application.TaskManagement.Labels;
using TaskFlow.Domain.IdentityAccess;
using Label = TaskFlow.Domain.TaskManagement.Label;
using LabelId = TaskFlow.Domain.TaskManagement.LabelId;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ILabelRepository"/> over <see cref="AppDbContext"/>. The context is
/// the Wolverine-integrated scoped DbContext, so writes participate in the per-message transaction/outbox.
/// All reads are owner-scoped (Tier A, FR-065).
/// </summary>
public sealed class LabelRepository(AppDbContext db) : ILabelRepository
{
    public Task<Label?> FindOwnedAsync(LabelId id, UserId owner, CancellationToken cancellationToken) =>
        db.Labels.FirstOrDefaultAsync(l => l.Id == id && l.OwnerId == owner, cancellationToken);

    public async Task<IReadOnlyList<Label>> ListForOwnerAsync(UserId owner, CancellationToken cancellationToken) =>
        await db.Labels
            .Where(l => l.OwnerId == owner)
            .OrderBy(l => l.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlySet<LabelId>> ListIdsForOwnerAsync(UserId owner, CancellationToken cancellationToken)
    {
        var ids = await db.Labels
            .Where(l => l.OwnerId == owner)
            .Select(l => l.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return ids.ToHashSet();
    }

    public Task<bool> ExistsByNormalizedNameForOwnerAsync(UserId owner, string nameNormalized, LabelId? excludingId, CancellationToken cancellationToken) =>
        db.Labels.AnyAsync(
            l => l.OwnerId == owner
                && l.NameNormalized == nameNormalized
                && (excludingId == null || l.Id != excludingId.Value),
            cancellationToken);

    public void Add(Label label) => db.Labels.Add(label);

    public void Remove(Label label) => db.Labels.Remove(label);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // `labels` has TWO unique constraints: PK_labels (id) and ux_labels_owner_name (owner_id,
            // name_normalized). A concurrent same-id PUT or a concurrent same-name create/rename loses the
            // race here (the in-handler pre-check has a TOCTOU window the DB index closes). DETACH the rejected
            // entity (it is tracked Added/Modified, so Wolverine's AutoApplyTransactions would re-attempt the
            // write → an uncaught 500, and EF identity-resolution would shadow the handler's re-resolve), then
            // translate to the Application-layer signal the handlers re-resolve/map (mirrors the slice-002
            // DuplicateTaskIdException translation; clean-architecture dependency direction).
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            throw new DuplicateLabelException("A label uniqueness constraint was violated.", ex);
        }
    }
}
