using System.Text.Json.Serialization;
using Cycle = TaskFlow.Domain.TaskManagement.Cycle;
using CycleStatus = TaskFlow.Domain.TaskManagement.CycleStatus;

namespace TaskFlow.Application.TaskManagement.Cycles;

/// <summary>
/// The per-status breakdown of a cycle's computed metrics (slice 011, D6/FR-026). Wire keys are
/// the task-status tokens (<c>in_progress</c>, not <c>inProgress</c>) so the UI maps them 1:1.
/// </summary>
public sealed record CycleBreakdownResponse
{
    public required int Backlog { get; init; }

    public required int Todo { get; init; }

    [JsonPropertyName("in_progress")]
    public required int InProgress { get; init; }

    public required int Done { get; init; }

    public required int Cancelled { get; init; }
}

/// <summary>
/// A cycle's computed TEAM-WIDE metrics (D6/D10): counts over ALL non-deleted assigned tasks,
/// regardless of author. Never stored — computed in the list query. The client derives
/// <c>percentDone = done / max(1, total − cancelled)</c> (cancelled tasks are not outstanding
/// work, data-model.md).
/// </summary>
public sealed record CycleMetricsResponse
{
    public required int Total { get; init; }

    public required int Done { get; init; }

    public required CycleBreakdownResponse Breakdown { get; init; }

    /// <summary>Projects the repository counts to the wire metrics.</summary>
    public static CycleMetricsResponse From(CycleTaskCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        return new CycleMetricsResponse
        {
            Total = counts.Total,
            Done = counts.Done,
            Breakdown = new CycleBreakdownResponse
            {
                Backlog = counts.Backlog,
                Todo = counts.Todo,
                InProgress = counts.InProgress,
                Done = counts.Done,
                Cancelled = counts.Cancelled,
            },
        };
    }
}

/// <summary>
/// The <c>CycleResponse</c> read model (slice 011, contracts/cycles-api.md). <c>createdAt</c> is
/// exposed so the client-side D5 tiebreaker has its data (the server list is already D5-ordered;
/// client sorting is defensive only). The name is user-authored — output-encoded on render (FR-099).
/// </summary>
public sealed record CycleResponse
{
    /// <summary>The client-generated UUIDv7 identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>The cycle name (≤ 200 chars). Untrusted content — sanitized on output (FR-099).</summary>
    public required string Name { get; init; }

    /// <summary>Start instant (UTC; Warsaw-midnight date-only convention, D18).</summary>
    public required DateTime StartDate { get; init; }

    /// <summary>End instant (UTC). Days-remaining is client-computed against Warsaw (FR-092).</summary>
    public required DateTime EndDate { get; init; }

    /// <summary>Lifecycle status: <c>planned | active | closed</c> (lowercase wire token).</summary>
    public required string Status { get; init; }

    /// <summary>Optimistic-concurrency token; incremented on every mutating write.</summary>
    public required int Version { get; init; }

    /// <summary>UTC creation timestamp — the D5 ordering tiebreaker.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>Computed team-wide metrics (D6).</summary>
    public required CycleMetricsResponse Metrics { get; init; }

    /// <summary>Projects a <see cref="Cycle"/> aggregate + its computed counts to the wire model.</summary>
    public static CycleResponse From(Cycle cycle, CycleTaskCounts counts)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        return new CycleResponse
        {
            Id = cycle.Id.Value,
            Name = cycle.Name,
            StartDate = cycle.StartDate,
            EndDate = cycle.EndDate,
            Status = ToWireStatus(cycle.Status),
            Version = cycle.Version,
            CreatedAt = cycle.CreatedAt,
            Metrics = CycleMetricsResponse.From(counts),
        };
    }

    /// <summary>Maps the domain status to its lowercase wire token (the CycleConfiguration db convention).</summary>
    private static string ToWireStatus(CycleStatus status) => status switch
    {
        CycleStatus.Planned => "planned",
        CycleStatus.Active => "active",
        CycleStatus.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown cycle status."),
    };
}

/// <summary>
/// The <c>CloseCycleResponse</c> envelope (slice 011, D4): the closed cycle + the rollover counts
/// for the confirmation toast / polite announcement copy (FR-101).
/// </summary>
public sealed record CloseCycleResponse
{
    /// <summary>The closed cycle (post-rollover metrics).</summary>
    public required CycleResponse Cycle { get; init; }

    /// <summary>Incomplete tasks reassigned to the next planned cycle.</summary>
    public required int RolledToNext { get; init; }

    /// <summary>Incomplete tasks returned to the cycle backlog (assignment cleared).</summary>
    public required int RolledToBacklog { get; init; }

    /// <summary>Incomplete tasks kept in the closed cycle with the carried-over flag.</summary>
    public required int Kept { get; init; }
}
