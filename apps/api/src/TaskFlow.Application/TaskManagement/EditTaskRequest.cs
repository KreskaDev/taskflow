using System.Diagnostics.CodeAnalysis;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The wire body for <c>PATCH /api/tasks/{id}/edit</c> (contracts/openapi.yaml <c>EditTaskRequest</c>,
/// slice 005, AS-06/07/08). WHOLE-OBJECT REPLACE (research R4): every editable field is a REQUIRED key with
/// a nullable value (<see cref="Title"/> required-non-null; the rest required-but-nullable), so the body
/// fully describes the post-edit state and an omitted field is a 422, never a silent null (mirrors slice-004
/// <c>EditProjectRequest</c>). The task <c>id</c> is carried in the route; the caller is resolved from
/// <c>ICurrentUser</c>. Named to match the OpenAPI schema component so the auto-emitted client schema aligns.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record EditTaskRequest
{
    /// <summary>The new title; trimmed-non-empty and ≤ 500 chars (required-non-null).</summary>
    public required string Title { get; init; }

    /// <summary>The new description (markdown source), or null. ≤ 8000 chars (R3).</summary>
    public required string? Description { get; init; }

    /// <summary>The new priority token (<c>P0</c>–<c>P3</c>), or null (R2).</summary>
    public required string? Priority { get; init; }

    /// <summary>The new client-resolved due-date UTC instant, or null. Paired with <see cref="DueHasTime"/>.</summary>
    public required DateTime? DueDate { get; init; }

    /// <summary>The <c>has_time</c> flag, or null. Paired with <see cref="DueDate"/>.</summary>
    public required bool? DueHasTime { get; init; }

    /// <summary>The owning project, or null for the Inbox. Reuses the move-to-project ownership check on an actual move.</summary>
    public required Guid? ProjectId { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token (R4); a stale value → 409.</summary>
    public required int Version { get; init; }
}
