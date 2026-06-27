using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.TaskManagement;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Archives the caller's own project (FR-013/AS-05, contracts/openapi.yaml <c>archiveProject</c>) — a
/// REVERSIBLE state (sets <c>archived_at</c>, hidden from default views, R2), under the optimistic
/// <c>version</c> guard. When the project has child projects, <see cref="ChildDisposition"/> is REQUIRED
/// (AS-10) — cascade (archive the children too) or orphan-to-top (promote them). The caller is resolved
/// from <see cref="ICurrentUser"/>.
/// </summary>
public sealed record ArchiveProject
{
    /// <summary>The project identity, carried in the route.</summary>
    public required ProjectId Id { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token; a stale value → 409.</summary>
    public required int Version { get; init; }

    /// <summary>The child disposition (AS-10); REQUIRED when the project has children (enforced in the handler).</summary>
    public string? ChildDisposition { get; init; }
}

/// <summary>
/// Handles <see cref="ArchiveProject"/> under the optimistic <c>version</c> rule. Authentication is
/// enforced upstream by the deny-by-default middleware.
/// </summary>
/// <remarks>
/// Decision path: owner-scoped + NON-deleted load (foreign/absent/tombstoned → 404, R13); stale version
/// → 409 BEFORE any mutation; apply the child disposition (AS-10) — when children exist a missing/invalid
/// disposition → 422, cascade ARCHIVES the children (reversible fate, R5); then archive the parent. All in
/// the per-message transaction; the interleaved-race backstop is at <c>ProjectRepository.SaveChangesAsync</c>.
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-002 handlers).")]
public static class ArchiveProjectHandler
{
    public static async Task<ProjectResponse> Handle(
        ArchiveProject command,
        ICurrentUser currentUser,
        IProjectRepository projects,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(projects);

        var owner = currentUser.Id;

        var project = await projects
            .FindOwnedAsync(command.Id, owner, cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
        {
            throw new NotFoundException();
        }

        if (project.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        var utcNow = DateTime.UtcNow;

        // Child disposition (AS-10), applied BEFORE the parent's archive. cascadeArchive: true — archiving a
        // parent archives the cascaded children (the subtree shares the parent's reversible fate, R5).
        await ProjectDispositions
            .ApplyChildDispositionAsync(command.Id, owner, command.ChildDisposition, cascadeArchive: true, utcNow, projects, cancellationToken)
            .ConfigureAwait(false);

        project.Archive(utcNow);
        await projects.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ProjectResponse.From(project);
    }
}
