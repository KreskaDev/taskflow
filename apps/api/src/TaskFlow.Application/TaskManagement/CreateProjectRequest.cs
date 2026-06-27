using System.Diagnostics.CodeAnalysis;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The wire body for <c>PUT /api/projects/{id}</c> (contracts/openapi.yaml <c>CreateProjectRequest</c>).
/// The project <c>id</c> is carried in the route, NOT the body; the caller is resolved from
/// <c>ICurrentUser</c> in the handler — so the body carries EXACTLY the client-authoritative create
/// fields. The endpoint maps this body + the route id into the <see cref="CreateProject"/> command.
/// Named to match the OpenAPI schema component so the auto-emitted client schema stays aligned.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record CreateProjectRequest
{
    /// <summary>The project name; trimmed-non-empty and ≤ 200 chars.</summary>
    public required string Name { get; init; }

    /// <summary>A preset color token (R10/ASM-04).</summary>
    public required string Color { get; init; }

    /// <summary>A preset icon token (R10/ASM-04).</summary>
    public required string Icon { get; init; }

    /// <summary>
    /// The top-level parent id, or null for a top-level project (one-level rule, R3). Plain nullable
    /// (NOT C# <c>required</c>) so it emits OPTIONAL in the client schema — absent = top-level on create.
    /// </summary>
    public Guid? ParentId { get; init; }
}
