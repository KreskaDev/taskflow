using System.Diagnostics.CodeAnalysis;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The wire body for <c>PATCH /api/tasks/{id}/priority</c> (contracts/openapi.yaml <c>SetPriorityRequest</c>,
/// slice 005, AS-04). The task <c>id</c> is carried in the route, NOT the body; the caller is resolved from
/// <c>ICurrentUser</c> in the handler. Named to match the OpenAPI schema component so the auto-emitted client
/// schema stays aligned.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record SetPriorityRequest
{
    /// <summary>Required key; nullable value — a <c>P0</c>–<c>P3</c> token, or null to clear (R2).</summary>
    public required string? Priority { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token (R4); a stale value → 409.</summary>
    public required int Version { get; init; }
}
