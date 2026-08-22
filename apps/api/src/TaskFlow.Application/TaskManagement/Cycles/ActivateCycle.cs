using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Errors;
using CycleStatus = TaskFlow.Domain.TaskManagement.CycleStatus;

namespace TaskFlow.Application.TaskManagement.Cycles;

/// <summary>
/// The manual planned → active transition (slice 011, contracts/cycles-api.md
/// <c>activateCycle</c>, D3). Team-wide authorized (spec IX); authentication upstream.
/// </summary>
public sealed record ActivateCycle
{
    /// <summary>The cycle identity, carried in the route.</summary>
    public required Domain.TaskManagement.CycleId Id { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token.</summary>
    public required int Version { get; init; }
}

/// <summary>
/// Handles <see cref="ActivateCycle"/>: load (404) → version-compare (409) → planned-only guard
/// (422 <c>cycle_not_planned</c>) → single-active check (409 <c>cycle_active_conflict</c>; the
/// <c>ix_cycles_single_active</c> partial unique index wins any interleaving race at the
/// repository seam, D3) → apply + persist.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen.")]
public static class ActivateCycleHandler
{
    public static async Task<CycleResponse> Handle(
        ActivateCycle command,
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

        if (cycle.Status != CycleStatus.Planned)
        {
            throw new CycleNotPlannedException();
        }

        var active = await cycles.FindActiveAsync(cancellationToken).ConfigureAwait(false);
        if (active is not null)
        {
            throw new CycleActiveConflictException();
        }

        cycle.Activate(DateTime.UtcNow);
        await cycles.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var counts = await cycles.CountTasksForCycleAsync(cycle.Id.Value, cancellationToken).ConfigureAwait(false);
        return CycleResponse.From(cycle, counts);
    }
}
