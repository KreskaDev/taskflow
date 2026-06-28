using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.TaskManagement;
using Wolverine;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Re-personalizes a shared project (contracts/openapi.yaml <c>unshareProject</c>, research R3, FR-058):
/// flips <c>visibility</c> back to <c>personal</c> and removes ALL membership rows in the same transaction
/// — every former member loses ALL access immediately (R10). The owner and the project's tasks are
/// retained. An owner-only MANAGE op on a shared project: a member with an insufficient role (editor/viewer)
/// → <b>403</b>, a non-member → <b>404</b> (research R9). VERSIONED: a stale <see cref="Version"/> → 409.
/// Raises <c>ProjectUnshared</c> (R13).
/// </summary>
public sealed record UnshareProject
{
    /// <summary>The project identity, carried in the route.</summary>
    public required ProjectId Id { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token; a stale value → 409.</summary>
    public required int Version { get; init; }
}

/// <summary>
/// Handles <see cref="UnshareProject"/>: member-readable load (non-member/foreign → 404), owner-role gate
/// (editor/viewer member → 403), version guard (stale → 409), <see cref="Project.Unshare"/> + revoke-all,
/// then drain <c>ProjectUnshared</c> to the outbox and commit — all in the per-message transaction.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-004 handlers).")]
public static class UnshareProjectHandler
{
    public static async Task<ProjectResponse> Handle(
        UnshareProject command,
        ICurrentUser currentUser,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        IResourceAuthorizationPolicy authorization,
        IMessageContext messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(messages);

        var caller = currentUser.Id;

        var project = await projects.FindReadableAsync(command.Id, caller, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            throw new NotFoundException();
        }

        var memberships = await members.ListByProjectAsync(command.Id, cancellationToken).ConfigureAwait(false);

        // Manage op → owner-only on a shared project (editor/viewer member → 403; the non-member 404 was
        // already produced by the readable load). Authorization precedes the concurrency check.
        authorization.RequireRole(project, memberships, EffectiveRole.Owner);

        if (project.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        // Already-personal → the domain pre-state guard throws; surface a clean 422, never a 500. (A personal
        // project has no /members surface, so the manage-role gate above admits only its owner here.)
        try
        {
            project.Unshare(DateTime.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            throw new ValidationException([new FluentValidation.Results.ValidationFailure("visibility", ex.Message)]);
        }

        await members.RemoveAllForProjectAsync(command.Id, cancellationToken).ConfigureAwait(false);

        await DomainEventDispatch.PublishAndClearAsync(project, messages, cancellationToken).ConfigureAwait(false);
        await projects.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ProjectResponse.From(project, EffectiveRole.Owner);
    }
}
