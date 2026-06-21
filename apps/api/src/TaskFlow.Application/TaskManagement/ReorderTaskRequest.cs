using System.Diagnostics.CodeAnalysis;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The wire body for <c>PATCH /api/tasks/{id}/position</c> (contracts/openapi.yaml <c>ReorderTaskRequest</c>).
/// The task <c>id</c> is carried in the route, NOT the body; the caller is resolved from
/// <c>ICurrentUser</c> in the handler. The endpoint maps this body + the route id into the
/// <c>ReorderTask</c> command. Named to match the OpenAPI schema component so the auto-emitted client
/// schema stays aligned.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record ReorderTaskRequest
{
    /// <summary>The new client-computed fractional-indexing rank string the task moves to (R5).</summary>
    public required string Position { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token (R4); a stale value → 409.</summary>
    public required int Version { get; init; }
}
