using System.Diagnostics.CodeAnalysis;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The wire body for <c>PATCH /api/tasks/{id}/cycle</c> (contracts/task-cycle.md
/// <c>SetTaskCycleRequest</c>). The task <c>id</c> is carried in the route; the caller is
/// resolved from <c>ICurrentUser</c>. <c>cycleId = null</c> clears the assignment (FR-016).
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record SetTaskCycleRequest
{
    /// <summary>The target cycle (any status, incl. closed), or null for the cycle backlog.</summary>
    public required Guid? CycleId { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token; a stale value → 409.</summary>
    public required int Version { get; init; }
}
