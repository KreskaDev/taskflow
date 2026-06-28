using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.TaskManagement;
using TaskFlow.Domain.TaskManagement.Events;
using Wolverine;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// A non-owner member leaves a shared project — self-service (contracts/openapi.yaml <c>leaveProject</c>,
/// research R10). The caller deletes their OWN membership row, losing ALL access immediately (R10);
/// raises <c>MembershipRevoked</c> (R13). Distinct from remove-member by WHO is authorized (self vs
/// owner-targets-another). If the CALLER is the owner → <b>409 `last_owner`</b> (the owner cannot leave;
/// transfer first — checked BEFORE the row lookup, the owner has no row, R7). A non-member caller → 404.
/// VERSIONED: stale → 409. The <c>version</c> rides a query param. Returns 204.
/// </summary>
public sealed record LeaveProject
{
    /// <summary>The shared project, carried in the route.</summary>
    public required ProjectId Id { get; init; }

    /// <summary>The Project optimistic-concurrency token (query param).</summary>
    public required int Version { get; init; }
}

/// <summary>Handles <see cref="LeaveProject"/> (see the command summary for the decision path).</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-004 handlers).")]
public static class LeaveProjectHandler
{
    public static async Task Handle(
        LeaveProject command,
        ICurrentUser currentUser,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        IMessageContext messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(messages);

        var caller = currentUser.Id;

        // A non-member (or foreign) project → 404; the owner and any member are both "readable".
        var project = await projects.FindReadableAsync(command.Id, caller, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            throw new NotFoundException();
        }

        // The owner cannot leave (the readable load admitted them; the owner has no row) → last_owner, BEFORE
        // the row lookup (R7).
        Project.EnsureNotLastOwner(project, caller);

        if (project.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        var row = await members.FindAsync(command.Id, caller, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            // Defensive: a non-owner who is readable must have a row; absent → not_found.
            throw new NotFoundException();
        }

        members.Remove(row);
        project.RecordMembershipChange(DateTime.UtcNow);

        await messages.PublishAsync(new MembershipRevoked(command.Id, caller)).ConfigureAwait(false);
        await members.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
