using System.Diagnostics.CodeAnalysis;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The wire body for <c>PATCH /api/tasks/{id}/project</c> (contracts/openapi.yaml
/// <c>MoveTaskToProjectRequest</c>, slice 004 US2). The task <c>id</c> is carried in the route, NOT the
/// body; the caller is resolved from <c>ICurrentUser</c> in the handler. The endpoint maps this body + the
/// route id into the <c>MoveTaskToProject</c> command. Named to match the OpenAPI schema component so the
/// auto-emitted client schema stays aligned.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record MoveTaskToProjectRequest
{
    /// <summary>The target project id, or null for the Inbox (R7).</summary>
    public Guid? ProjectId { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token (R4/R7); a stale value → 409.</summary>
    public required int Version { get; init; }
}
