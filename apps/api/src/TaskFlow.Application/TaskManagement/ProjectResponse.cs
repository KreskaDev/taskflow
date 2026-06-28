using TaskFlow.Application.Authorization;
using TaskFlow.Domain.TaskManagement;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The lean Project read model (contracts/openapi.yaml <c>ProjectResponse</c>, R6/R16), shared by
/// create, edit, archive/unarchive, and the list. Carries the optimistic <see cref="Version"/> token
/// so the 409 path round-trips. NEVER exposes <c>ownerId</c> (always the caller) or <c>deletedAt</c>
/// (soft-deleted rows are never returned) — the read-model leak rule (data-model §4).
/// </summary>
public sealed record ProjectResponse
{
    /// <summary>The client-generated UUIDv7 identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>The project name (≤ 200 chars); output-encoded on render (FR-099).</summary>
    public required string Name { get; init; }

    /// <summary>The preset color token (R10/ASM-04).</summary>
    public required string Color { get; init; }

    /// <summary>The preset icon token (R10/ASM-04).</summary>
    public required string Icon { get; init; }

    /// <summary>The parent project id, or null for a top-level project (one-level rule, R3).</summary>
    public Guid? ParentId { get; init; }

    /// <summary>Visibility — <c>personal</c> or <c>shared</c> (research R3).</summary>
    public required string Visibility { get; init; }

    /// <summary>
    /// The CALLER's effective role for this project (<c>owner | editor | viewer</c>, research R17). For a
    /// personal project the caller is always the owner (<c>owner</c>). Drives client-side UI gating (a
    /// viewer sees read-only); NOT the security boundary (server FR-068 is authoritative). Nullable for
    /// forward-compatibility, but always populated by the slice-007 projections.
    /// </summary>
    public string? Role { get; init; }

    /// <summary>Archive timestamp (UTC); null = active, non-null = archived (reversible state, R2).</summary>
    public DateTime? ArchivedAt { get; init; }

    /// <summary>Optimistic-concurrency token; incremented on every mutating write.</summary>
    public required int Version { get; init; }

    /// <summary>UTC creation timestamp.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>UTC last-mutation timestamp.</summary>
    public required DateTime UpdatedAt { get; init; }

    /// <summary>
    /// Projects a <see cref="Project"/> aggregate to its lean wire model (mirrors <c>TaskResponse.From</c>).
    /// The caller's effective <paramref name="role"/> defaults to <see cref="EffectiveRole.Owner"/>: every
    /// slice-004 caller (create/edit/archive/unarchive/owned-list) is the owner of the projected row, so the
    /// default is correct without threading the policy through those handlers. The shared-projects branch of
    /// <c>GetMyProjects</c> passes the resolved <c>editor</c>/<c>viewer</c> role explicitly (R17).
    /// </summary>
    public static ProjectResponse From(Project project, EffectiveRole role = EffectiveRole.Owner)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new ProjectResponse
        {
            Id = project.Id.Value,
            Name = project.Name,
            Color = project.Color,
            Icon = project.Icon,
            ParentId = project.ParentId?.Value,
            Visibility = project.Visibility,
            Role = role.ToWireValue(),
            ArchivedAt = project.ArchivedAt,
            Version = project.Version,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt,
        };
    }
}
