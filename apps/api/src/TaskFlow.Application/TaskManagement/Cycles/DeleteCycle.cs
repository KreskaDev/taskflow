using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Errors;
using CycleStatus = TaskFlow.Domain.TaskManagement.CycleStatus;

namespace TaskFlow.Application.TaskManagement.Cycles;

/// <summary>
/// Hard-deletes a cycle behind the FR-019/FR-020 guards (slice 011, contracts/cycles-api.md
/// <c>deleteCycle</c>, EC-04): an ACTIVE cycle never deletes (close it first); a planned/closed
/// cycle deletes only when NO task references it. Team-wide authorized (spec IX).
/// </summary>
public sealed record DeleteCycle
{
    /// <summary>The cycle identity, carried in the route.</summary>
    public required Domain.TaskManagement.CycleId Id { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token (the <c>?version=</c> query).</summary>
    public required int Version { get; init; }
}

/// <summary>
/// Handles <see cref="DeleteCycle"/>: load (404) → version-compare (409) → active guard (422
/// <c>cycle_active_delete_forbidden</c>) → emptiness guard (422 <c>cycle_not_empty</c>; counts
/// ALL referencing rows incl. tombstones so the friendly guard agrees with the FK RESTRICT
/// backstop) → hard delete. The repository translates a concurrent-assignment FK violation back
/// to <c>cycle_not_empty</c> (TOCTOU closed at the database).
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen.")]
public static class DeleteCycleHandler
{
    public static async Task Handle(
        DeleteCycle command,
        ICycleRepository cycles,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(cycles);

        var cycle = await cycles.FindByIdAsync(command.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException();

        if (cycle.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        if (cycle.Status == CycleStatus.Active)
        {
            throw new CycleActiveDeleteForbiddenException();
        }

        var referencing = await cycles.CountTasksReferencingAsync(cycle.Id.Value, cancellationToken).ConfigureAwait(false);
        if (referencing > 0)
        {
            throw new CycleNotEmptyException();
        }

        cycles.Remove(cycle);
        await cycles.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
