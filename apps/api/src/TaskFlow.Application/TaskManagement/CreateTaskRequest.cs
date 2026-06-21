using System.Diagnostics.CodeAnalysis;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The wire body for <c>PUT /api/tasks/{id}</c> (contracts/openapi.yaml <c>CreateTaskRequest</c>).
/// The task <c>id</c> is carried in the route, NOT the body; the caller is resolved from
/// <c>ICurrentUser</c> in the handler — so the body carries EXACTLY the two client-authoritative
/// create fields. The endpoint maps this body + the route id into the <see cref="CreateTask"/>
/// command. Named to match the OpenAPI schema component so the auto-emitted client schema stays aligned.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record CreateTaskRequest
{
    /// <summary>The task title; trimmed-non-empty and ≤ 500 chars (FR-001).</summary>
    public required string Title { get; init; }

    /// <summary>The client-computed fractional-indexing rank string (R5).</summary>
    public required string Position { get; init; }

    /// <summary>
    /// The resolved due-date instant in UTC (FR-092, R8), or null for no due date. The client
    /// parses the trailing Polish phrase, resolves it against Europe/Warsaw, and sends the UTC
    /// instant (a <c>Z</c>-form ISO-8601 string). Paired with <see cref="DueHasTime"/> — both null
    /// or both non-null. Plain nullable (NOT C# <c>required</c>) so it emits OPTIONAL in the client schema.
    /// </summary>
    public DateTime? DueDate { get; init; }

    /// <summary>
    /// The <c>DueDate.has_time</c> flag (R2/R8): <c>true</c> = date-time ("po 17"), <c>false</c> =
    /// date-only ("jutro"), null for no due date. Paired with <see cref="DueDate"/> — both null or
    /// both non-null. Plain nullable (NOT C# <c>required</c>) so it emits OPTIONAL in the client schema.
    /// </summary>
    public bool? DueHasTime { get; init; }
}
