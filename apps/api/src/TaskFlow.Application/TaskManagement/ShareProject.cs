using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.TaskManagement;
using Wolverine;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Converts the caller's own personal project to <c>shared</c> (contracts/openapi.yaml <c>shareProject</c>,
/// research R3, FR-058) — the first legal write of the <c>shared</c> visibility value. Owner-only: the
/// project is still personal, so it is loaded owner-scoped and a non-owner resolves to <b>404</b> (the
/// slice-004 ownership posture — existence not disclosed), never 403. VERSIONED: a stale <see cref="Version"/>
/// → 409. The caller is resolved from <see cref="ICurrentUser"/>.
/// </summary>
public sealed record ShareProject
{
    /// <summary>The project identity, carried in the route.</summary>
    public required ProjectId Id { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token; a stale value → 409.</summary>
    public required int Version { get; init; }
}

/// <summary>
/// Handles <see cref="ShareProject"/>: owner-scoped load (foreign/absent/tombstoned → 404), version guard
/// (stale → 409), <see cref="Project.Share"/> (personal → shared, raising <c>ProjectShared</c>), then drain
/// the event to the outbox and commit — all in the per-message transaction. Authentication is enforced
/// upstream by the deny-by-default middleware.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-004 handlers).")]
public static class ShareProjectHandler
{
    public static async Task<ProjectResponse> Handle(
        ShareProject command,
        ICurrentUser currentUser,
        IProjectRepository projects,
        IMessageContext messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(messages);

        var caller = currentUser.Id;

        // Owner-scoped load: a personal project has no members, so a non-owner is not_found (the contract
        // says /share by a non-owner → 404, never 403).
        var project = await projects.FindOwnedAsync(command.Id, caller, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            throw new NotFoundException();
        }

        if (project.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        // Already-shared → the domain pre-state guard throws; surface a clean 422 (a client state error),
        // never a 500 (the UI only offers Share on a personal project, but the endpoint must be robust).
        try
        {
            project.Share(DateTime.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            throw new ValidationException([new FluentValidation.Results.ValidationFailure("visibility", ex.Message)]);
        }

        await DomainEventDispatch.PublishAndClearAsync(project, messages, cancellationToken).ConfigureAwait(false);
        await projects.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ProjectResponse.From(project, EffectiveRole.Owner);
    }
}
