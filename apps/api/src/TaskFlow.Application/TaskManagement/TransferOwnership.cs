using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;
using Wolverine;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Transfers ownership of a shared project to a current member (contracts/openapi.yaml
/// <c>transferProjectOwnership</c>, research R6, FR-094) — the only legal move of the immutable
/// <c>ownerId</c>. Owner-only (editor/viewer member → 403, non-member → 404). In one transaction: the new
/// owner's membership row is REMOVED (the owner has no row), <c>ownerId</c> is reassigned, and the prior
/// owner is DEMOTED to a new <c>editor</c> row (ADR-0003). The target MUST already be a current member — a
/// non-member or the current owner → <b>422 `validation_failed`</b> (R6). Raises <c>OwnerTransferred</c>
/// (R13). VERSIONED: stale → 409. Returns the project with the caller's NEW effective role (<c>editor</c>).
/// </summary>
public sealed record TransferOwnership
{
    /// <summary>The shared project, carried in the route.</summary>
    public required ProjectId Id { get; init; }

    /// <summary>The current member to make the new owner.</summary>
    public required UserId NewOwnerId { get; init; }

    /// <summary>The Project optimistic-concurrency token.</summary>
    public required int Version { get; init; }
}

/// <summary>Handles <see cref="TransferOwnership"/> (see the command summary for the decision path).</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-004 handlers).")]
public static class TransferOwnershipHandler
{
    public static async Task<ProjectResponse> Handle(
        TransferOwnership command,
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

        var project = await MembershipGuards
            .LoadManageableSharedProjectAsync(command.Id, currentUser, projects, members, authorization, cancellationToken)
            .ConfigureAwait(false);

        if (project.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        // The target must be a CURRENT member and not the current owner (R6) — a pure reassignment within the
        // existing access set (no implicit invite). Both collapse to one 422 field shape on `userId`.
        if (command.NewOwnerId == project.OwnerId)
        {
            throw TargetValidation("That member is already the owner.");
        }

        var targetRow = await members.FindAsync(command.Id, command.NewOwnerId, cancellationToken).ConfigureAwait(false);
        if (targetRow is null)
        {
            throw TargetValidation("The new owner must already be a current member of the project.");
        }

        var utcNow = DateTime.UtcNow;
        var priorOwner = project.OwnerId;

        // One transaction: drop the new owner's row, move ownerId, insert an editor row for the prior owner.
        members.Remove(targetRow);
        project.TransferOwnerTo(command.NewOwnerId, utcNow);
        members.Add(ProjectMembership.Create(ProjectMembershipId.New(), command.Id, priorOwner, MembershipRoles.Editor, utcNow));

        await DomainEventDispatch.PublishAndClearAsync(project, messages, cancellationToken).ConfigureAwait(false);
        await members.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The caller (prior owner) is now an editor.
        return ProjectResponse.From(project, EffectiveRole.Editor);
    }

    // The field key is the wire field name (camelCase "userId"), matching the request body's JSON field.
    private static ValidationException TargetValidation(string message) =>
        new([new FluentValidation.Results.ValidationFailure("userId", message)]);
}
