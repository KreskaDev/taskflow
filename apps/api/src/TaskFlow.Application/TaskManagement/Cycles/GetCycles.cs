using System.Diagnostics.CodeAnalysis;

namespace TaskFlow.Application.TaskManagement.Cycles;

/// <summary>
/// The cycle list + computed team-wide metrics (slice 011, contracts/cycles-api.md
/// <c>listCycles</c>, D5/D6/D10): ALL cycles in <c>(StartDate, CreatedAt, Id)</c> order, each
/// with counts over ALL non-deleted assigned tasks (the numbers are shared across admitted
/// users — cycle reads are NOT caller-scoped, spec IX). No pagination (ASM-10 scale).
/// </summary>
public sealed record GetCycles;

/// <summary>Handles <see cref="GetCycles"/>: one ordered list + ONE grouped count query (no N+1, D6).</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen.")]
public static class GetCyclesHandler
{
    public static async Task<IReadOnlyList<CycleResponse>> Handle(
        GetCycles query,
        ICycleRepository cycles,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cycles);

        var all = await cycles.ListAllAsync(cancellationToken).ConfigureAwait(false);
        var counts = await cycles.CountTasksByCycleAsync(cancellationToken).ConfigureAwait(false);

        return all
            .Select(c => CycleResponse.From(
                c,
                counts.TryGetValue(c.Id.Value, out var cycleCounts) ? cycleCounts : CycleTaskCounts.Empty))
            .ToList();
    }
}
