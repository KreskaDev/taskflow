using System.Diagnostics.CodeAnalysis;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The wire body for <c>PATCH /api/projects/{id}</c> (contracts/openapi.yaml <c>EditProjectRequest</c>).
/// The project <c>id</c> is carried in the route, NOT the body; the caller is resolved from
/// <c>ICurrentUser</c> in the handler. WHOLE-OBJECT REPLACE (research R4): every mutable field —
/// including <see cref="ParentId"/> — is REQUIRED, so the body fully describes the post-edit state and an
/// omitted <see cref="ParentId"/> is a 422, never a silent un-parent. Named to match the OpenAPI schema
/// component so the auto-emitted client schema stays aligned.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record EditProjectRequest
{
    /// <summary>The new project name; trimmed-non-empty and ≤ 200 chars.</summary>
    public required string Name { get; init; }

    /// <summary>The new preset color token (R10/ASM-04).</summary>
    public required string Color { get; init; }

    /// <summary>The new preset icon token (R10/ASM-04).</summary>
    public required string Icon { get; init; }

    /// <summary>
    /// The new top-level parent id, or null for top-level (one-level rule, R3). REQUIRED — whole-object
    /// replace (R4): the form always re-sends the current parent, so an omitted field is a 422, never a
    /// silent demotion of a child to top-level.
    /// </summary>
    public required Guid? ParentId { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token (R4); a stale value → 409.</summary>
    public required int Version { get; init; }
}
