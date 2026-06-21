using System.Diagnostics.CodeAnalysis;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The wire body for <c>PATCH /api/tasks/{id}/status</c> (contracts/openapi.yaml <c>SetTaskStatusRequest</c>).
/// The task <c>id</c> is carried in the route, NOT the body; the caller is resolved from
/// <c>ICurrentUser</c> in the handler. The endpoint maps this body + the route id into the
/// <c>SetTaskDone</c> command. Named to match the OpenAPI schema component so the auto-emitted client
/// schema stays aligned.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record SetTaskStatusRequest
{
    /// <summary>The DESIRED status (<c>done</c>|<c>backlog</c> this slice); any other target → 422.</summary>
    public required string Status { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token (R4); a stale value → 409.</summary>
    public required int Version { get; init; }
}
