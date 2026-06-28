using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.TaskManagement;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Unarchives the caller's own project (AS-11, contracts/openapi.yaml <c>unarchiveProject</c>): clears
/// <c>archived_at</c>, under the optimistic <c>version</c> guard. Per R9, a child whose parent is STILL
/// archived/deleted is restored as TOP-LEVEL (its parent is nulled) rather than re-nested under a hidden
/// parent. The caller is resolved from <see cref="ICurrentUser"/>.
/// </summary>
public sealed record UnarchiveProject
{
    /// <summary>The project identity, carried in the route.</summary>
    public required ProjectId Id { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token; a stale value → 409.</summary>
    public required int Version { get; init; }
}

/// <summary>
/// Handles <see cref="UnarchiveProject"/> under the optimistic <c>version</c> rule. Authentication is
/// enforced upstream by the deny-by-default middleware.
/// </summary>
/// <remarks>
/// Decision path: owner-scoped + NON-deleted load (foreign/absent/tombstoned → 404, R13); stale version
/// → 409 BEFORE the mutation; resolve the R9 "parent still hidden" fact — a non-null parent that is absent
/// (deleted, so the deleted-filtered find returns null) OR still archived counts as hidden — and pass it
/// to <c>Project.Unarchive</c>, which nulls the parent in that case. Persist in the per-message transaction.
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-002 handlers).")]
public static class UnarchiveProjectHandler
{
    public static async Task<ProjectResponse> Handle(
        UnarchiveProject command,
        ICurrentUser currentUser,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        IResourceAuthorizationPolicy authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(authorization);

        var owner = currentUser.Id;

        // Manage-op visibility dispatch (R8/R9): personal arm unchanged (non-owner → 404); shared arm
        // owner-only (editor/viewer → 403, non-member → 404).
        var project = await MembershipGuards
            .LoadOwnerManagedProjectAsync(command.Id, currentUser, projects, members, authorization, cancellationToken)
            .ConfigureAwait(false);

        if (project.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        // R9: is the project's current parent still hidden (archived or deleted/gone)? A deleted parent is
        // filtered out by FindOwnedAsync (deleted_at), so a non-null parent that resolves to null is "gone";
        // a resolved parent with archived_at set is "still archived". Both → restore the child top-level.
        var parentStillHidden = false;
        if (project.ParentId is { } parentId)
        {
            var parent = await projects
                .FindOwnedAsync(parentId, owner, cancellationToken)
                .ConfigureAwait(false);
            parentStillHidden = parent is null || parent.ArchivedAt is not null;
        }

        project.Unarchive(parentStillHidden, DateTime.UtcNow);
        await projects.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ProjectResponse.From(project);
    }
}
