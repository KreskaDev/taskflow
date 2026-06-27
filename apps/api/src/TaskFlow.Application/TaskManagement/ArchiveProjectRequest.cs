using System.Diagnostics.CodeAnalysis;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The wire body for <c>PATCH /api/projects/{id}/archive</c> (contracts/openapi.yaml
/// <c>ArchiveProjectRequest</c>). The project <c>id</c> is carried in the route; the caller is resolved
/// from <c>ICurrentUser</c>. <see cref="ChildDisposition"/> is REQUIRED when the project has child
/// projects (AS-10) — that cross-row requirement is enforced in the handler, so the field is optional on
/// the wire (a top-level/childless project omits it). Named to match the OpenAPI schema component.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record ArchiveProjectRequest
{
    /// <summary>The caller's last-seen optimistic-concurrency token; a stale value → 409.</summary>
    public required int Version { get; init; }

    /// <summary>
    /// How to handle the project's child projects (AS-10): <c>cascade</c> (archive the children with the
    /// parent) or <c>orphan_to_top</c> (promote them to top-level). REQUIRED when the project has children
    /// (enforced in the handler); null/omitted for a childless project.
    /// </summary>
    public string? ChildDisposition { get; init; }
}

/// <summary>
/// The wire body for <c>PATCH /api/projects/{id}/unarchive</c> (contracts/openapi.yaml
/// <c>VersionOnlyRequest</c>): the optimistic <c>version</c> only.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record VersionOnlyRequest
{
    /// <summary>The caller's last-seen optimistic-concurrency token; a stale value → 409.</summary>
    public required int Version { get; init; }
}
