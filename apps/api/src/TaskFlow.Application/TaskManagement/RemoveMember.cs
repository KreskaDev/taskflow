using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;
using TaskFlow.Domain.TaskManagement.Events;
using Wolverine;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Removes a member from a shared project (contracts/openapi.yaml <c>removeProjectMember</c>, research
/// R10). Owner-only manage op (editor/viewer member → 403, non-member caller → 404). The removed user loses
/// ALL access immediately (R10) and the row deletion raises <c>MembershipRevoked</c> (R13). The OWNER as
/// target → <b>409 `last_owner`</b> (transfer first, checked BEFORE the row lookup, R7); a target who is
/// neither owner nor a member → 404. VERSIONED: stale → 409. The <c>version</c> rides a query param
/// (slice-004 DELETE posture). Returns 204.
/// </summary>
public sealed record RemoveMember
{
    /// <summary>The shared project, carried in the route.</summary>
    public required ProjectId Id { get; init; }

    /// <summary>The target member's User id, carried in the route.</summary>
    public required UserId TargetUserId { get; init; }

    /// <summary>The Project optimistic-concurrency token (query param).</summary>
    public required int Version { get; init; }
}

/// <summary>Handles <see cref="RemoveMember"/> (see the command summary for the decision path).</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-004 handlers).")]
public static class RemoveMemberHandler
{
    public static async Task Handle(
        RemoveMember command,
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

        // The last-owner guard is a permanent structural reject, so it precedes BOTH the version check and
        // the row lookup (R7) — consistent with LeaveProject and ChangeMemberRole.
        Project.EnsureNotLastOwner(project, command.TargetUserId);

        if (project.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        var row = await members.FindAsync(command.Id, command.TargetUserId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            throw new NotFoundException();
        }

        members.Remove(row);
        project.RecordMembershipChange(DateTime.UtcNow);

        await messages.PublishAsync(new MembershipRevoked(command.Id, command.TargetUserId)).ConfigureAwait(false);
        await members.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
