using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Application.IdentityAccess;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;
using TaskFlow.Domain.TaskManagement.Events;
using Wolverine;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Changes a member's assignable role between <c>editor</c> and <c>viewer</c> (contracts/openapi.yaml
/// <c>changeProjectMemberRole</c>, research R5). Owner-only manage op (editor/viewer member → 403,
/// non-member → 404). The target MUST be a current membership row; the OWNER as target → <b>409
/// `last_owner`</b> (checked BEFORE the row lookup — the owner has no row, R7); a target who is neither
/// owner nor a member → 404. Re-sending the current role is a no-op + version bump (R5). A <b>demotion</b>
/// (editor → viewer) raises <c>MembershipRevoked</c>; a promotion raises none (R5/H1). VERSIONED: stale → 409.
/// </summary>
public sealed record ChangeMemberRole
{
    /// <summary>The shared project, carried in the route.</summary>
    public required ProjectId Id { get; init; }

    /// <summary>The target member's User id, carried in the route.</summary>
    public required UserId TargetUserId { get; init; }

    /// <summary>The new assignable role (<c>editor</c> or <c>viewer</c>).</summary>
    public required string Role { get; init; }

    /// <summary>The Project optimistic-concurrency token.</summary>
    public required int Version { get; init; }
}

/// <summary>Boundary validation: the role is an assignable stored value (<c>owner</c> unrepresentable, R2).</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors slice-004 validators).")]
public sealed class ChangeMemberRoleValidator : AbstractValidator<ChangeMemberRole>
{
    public ChangeMemberRoleValidator() =>
        RuleFor(x => x.Role)
            .Must(MembershipRoles.IsAssignable)
            .WithMessage($"Role must be '{MembershipRoles.Editor}' or '{MembershipRoles.Viewer}'.");
}

/// <summary>Handles <see cref="ChangeMemberRole"/> (see the command summary for the decision path).</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-004 handlers).")]
public static class ChangeMemberRoleHandler
{
    public static async Task<MemberResponse> Handle(
        ChangeMemberRole command,
        ICurrentUser currentUser,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        IUserRepository users,
        IResourceAuthorizationPolicy authorization,
        IMessageContext messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(messages);

        var project = await MembershipGuards
            .LoadManageableSharedProjectAsync(command.Id, currentUser, projects, members, authorization, cancellationToken)
            .ConfigureAwait(false);

        // Owner-as-target → last_owner (the owner is not demotable; transfer first). The structural guard
        // precedes BOTH the version check and the row lookup (R7) — a permanent reject outranks a stale-token
        // retry, and is consistent with LeaveProject.
        Project.EnsureNotLastOwner(project, command.TargetUserId);

        if (project.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        var row = await members.FindAsync(command.Id, command.TargetUserId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            // Neither the owner (guarded above) nor a member → not_found (R5/R9).
            throw new NotFoundException();
        }

        var utcNow = DateTime.UtcNow;
        var wasEditor = row.Role == MembershipRoles.Editor;
        var isDemotion = wasEditor && command.Role == MembershipRoles.Viewer;

        row.ChangeRole(command.Role, utcNow);
        project.RecordMembershipChange(utcNow);

        // A demotion (editor → viewer) revokes the editor capability → MembershipRevoked (R5/H1). A promotion
        // (or a no-op re-send of the current role) is access-additive/neutral → no event.
        if (isDemotion)
        {
            await messages.PublishAsync(new MembershipRevoked(command.Id, command.TargetUserId)).ConfigureAwait(false);
        }

        await members.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var displayName = (await users.FindByIdAsync(command.TargetUserId, cancellationToken).ConfigureAwait(false))?.DisplayName
            ?? string.Empty;
        var role = command.Role == MembershipRoles.Editor ? EffectiveRole.Editor : EffectiveRole.Viewer;
        return MemberResponse.From(command.TargetUserId.Value, displayName, role);
    }
}
