using System.Diagnostics.CodeAnalysis;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The wire body for <c>PATCH /api/tasks/{id}/due-date</c> (contracts/openapi.yaml
/// <c>RescheduleDueDateRequest</c>, slice 005, AS-05). The task <c>id</c> is carried in the route, NOT the
/// body; the caller is resolved from <c>ICurrentUser</c> in the handler. The CLIENT resolves the Polish
/// phrase to a UTC instant (slice-003 parser); the SERVER re-validates it. Named to match the OpenAPI schema
/// component so the auto-emitted client schema stays aligned.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record RescheduleDueDateRequest
{
    /// <summary>Required key; nullable value — the client-resolved due-date UTC instant, or null to clear. Paired with <see cref="DueHasTime"/>.</summary>
    public required DateTime? DueDate { get; init; }

    /// <summary>Required key; nullable value — the <c>has_time</c> flag, or null when <see cref="DueDate"/> is null. Paired with <see cref="DueDate"/>.</summary>
    public required bool? DueHasTime { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token (R4); a stale value → 409.</summary>
    public required int Version { get; init; }
}
