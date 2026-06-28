using System.Diagnostics.CodeAnalysis;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The wire body for <c>PATCH /api/tasks/{id}/assignees</c> (contracts/openapi.yaml
/// <c>SetTaskAssigneesRequest</c>, slice 008). The task <c>id</c> is carried in the route; the caller is
/// resolved from <c>ICurrentUser</c>. WHOLE-SET replace (research R2): <see cref="AssigneeIds"/> is the
/// desired FULL assignee set. Named to match the OpenAPI schema component so the auto-emitted client
/// schema stays aligned.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record SetTaskAssigneesRequest
{
    /// <summary>The desired full assignee set (each MUST be a current member of the task's shared project). No duplicates; empty clears all.</summary>
    public required IReadOnlyList<Guid> AssigneeIds { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token (R4); a stale value → 409.</summary>
    public required int Version { get; init; }
}
