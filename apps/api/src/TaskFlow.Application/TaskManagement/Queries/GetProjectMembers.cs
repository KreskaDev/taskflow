using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Application.IdentityAccess;
using TaskFlow.Domain.IdentityAccess;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;
using DomainProject = TaskFlow.Domain.TaskManagement.Project;

namespace TaskFlow.Application.TaskManagement.Queries;

/// <summary>
/// Lists a shared project's composed members roster (contracts/openapi.yaml <c>listProjectMembers</c>,
/// research R17): the OWNER (from <c>ownerId</c>, effective role <c>owner</c>, <c>isOwner=true</c>) ∪ the
/// <c>editor</c>/<c>viewer</c> membership rows. Readable by ANY current member (viewer+); a non-member → 404
/// (existence not disclosed, R9). Surfaces the project <c>version</c> so leave/change-role/remove/transfer
/// callers carry the concurrency token (R11). Does NOT echo member emails (Constitution XI).
/// </summary>
public sealed record GetProjectMembers
{
    /// <summary>The project identity, carried in the route.</summary>
    public required ProjectId ProjectId { get; init; }
}

/// <summary>Handles <see cref="GetProjectMembers"/> (member-only read; composes owner ∪ rows).</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-004 query handlers).")]
public static class GetProjectMembersHandler
{
    public static async Task<MembersResponse> Handle(
        GetProjectMembers query,
        ICurrentUser currentUser,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        IUserRepository users,
        IResourceAuthorizationPolicy authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(authorization);

        var project = await projects.FindReadableAsync(query.ProjectId, currentUser.Id, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            throw new NotFoundException();
        }

        var memberships = await members.ListByProjectAsync(query.ProjectId, cancellationToken).ConfigureAwait(false);

        // Any current member (viewer+) may read the roster; a non-member was already 404'd by the load.
        authorization.RequireRole(project, memberships, EffectiveRole.Viewer);

        // The membership roster exists only on a shared project (a personal one has none).
        if (project.Visibility != DomainProject.SharedVisibility)
        {
            throw new NotFoundException();
        }

        // Resolve display names for the owner ∪ member rows in one batch (R17 — never emails).
        var ids = memberships.Select(m => m.UserId).Append(project.OwnerId).Distinct().ToList();
        var byId = (await users.ListByIdsAsync(ids, cancellationToken).ConfigureAwait(false))
            .ToDictionary(u => u.Id, u => u.DisplayName);

        var roster = new List<MemberResponse>
        {
            MemberResponse.From(project.OwnerId.Value, DisplayName(byId, project.OwnerId), EffectiveRole.Owner),
        };
        roster.AddRange(memberships.Select(m => MemberResponse.From(
            m.UserId.Value,
            DisplayName(byId, m.UserId),
            m.Role == TaskFlow.Domain.TaskManagement.MembershipRoles.Editor ? EffectiveRole.Editor : EffectiveRole.Viewer)));

        return new MembersResponse
        {
            ProjectId = project.Id.Value,
            Version = project.Version,
            Members = roster,
        };
    }

    private static string DisplayName(Dictionary<UserId, string> byId, UserId id) =>
        byId.TryGetValue(id, out var name) ? name : string.Empty;
}
