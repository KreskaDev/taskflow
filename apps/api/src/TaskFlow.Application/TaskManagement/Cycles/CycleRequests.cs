using System.Diagnostics.CodeAnalysis;

namespace TaskFlow.Application.TaskManagement.Cycles;

/// <summary>
/// The wire body for <c>PUT /api/cycles/{id}</c> (contracts/cycles-api.md <c>CreateCycleRequest</c>).
/// The cycle <c>id</c> is carried in the route (client-generated UUIDv7, idempotent PUT).
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record CreateCycleRequest
{
    /// <summary>The cycle name; trimmed-non-empty and ≤ 200 chars.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Start instant (UTC); must be strictly before <see cref="EndDate"/>. Nullable ON THE WIRE
    /// so a missing date reaches the FluentValidation boundary as a 422 <c>validation_failed</c>
    /// (a <c>required DateTime</c> would 400 at JSON binding with no errorCode).
    /// </summary>
    public DateTime? StartDate { get; init; }

    /// <summary>End instant (UTC). Overlap with other cycles is NOT validated (Clarifications). Nullable on the wire like <see cref="StartDate"/>.</summary>
    public DateTime? EndDate { get; init; }
}

/// <summary>
/// The wire body for <c>PATCH /api/cycles/{id}</c> (<c>EditCycleRequest</c>): a whole-object
/// replace of name + dates under OCC; legal in every status.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record EditCycleRequest
{
    /// <summary>The new name; trimmed-non-empty and ≤ 200 chars.</summary>
    public required string Name { get; init; }

    /// <summary>The new start instant (UTC); must stay strictly before <see cref="EndDate"/>. Nullable on the wire (see <see cref="CreateCycleRequest.StartDate"/>).</summary>
    public DateTime? StartDate { get; init; }

    /// <summary>The new end instant (UTC). Nullable on the wire.</summary>
    public DateTime? EndDate { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token; a stale value → 409.</summary>
    public required int Version { get; init; }
}

/// <summary>A single per-task override in the close review (<c>handle individually</c>, US-05.AS-04).</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as part of the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record CloseCycleOverride
{
    /// <summary>The task the override targets; must be caller-visible (404 posture otherwise, D10).</summary>
    public required Guid TaskId { get; init; }

    /// <summary>The per-task rollover choice: <c>next | backlog | keep</c>.</summary>
    public required string Choice { get; init; }
}

/// <summary>
/// The wire body for <c>PATCH /api/cycles/{id}/close</c> (<c>CloseCycleRequest</c>, D4): the bulk
/// rollover decision + optional per-task overrides, committed atomically with the status flip.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record CloseCycleRequest
{
    /// <summary>
    /// The bulk choice applied to every incomplete task not covered by an override:
    /// <c>next | backlog | keep</c>. Defaults to <c>keep</c> — the default exists for the
    /// pure-close path (zero incomplete tasks); the UI always sends an explicit choice otherwise.
    /// </summary>
    public string? Rollover { get; init; }

    /// <summary>Optional per-task overrides ("obsłuż pojedynczo"); may target only caller-visible tasks.</summary>
    public IReadOnlyList<CloseCycleOverride>? Overrides { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token; a stale value → 409.</summary>
    public required int Version { get; init; }
}
