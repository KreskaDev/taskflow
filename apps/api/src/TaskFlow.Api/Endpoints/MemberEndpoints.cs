using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.TaskManagement;
using TaskFlow.Application.TaskManagement.Queries;
using Wolverine;
using Wolverine.Http;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;
using UserId = TaskFlow.Domain.IdentityAccess.UserId;

namespace TaskFlow.Api.Endpoints;

/// <summary>
/// HTTP surface for a shared project's members resource (contracts/openapi.yaml, slice 007). Each endpoint
/// is a thin transport adapter dispatching through Wolverine's local message pipeline, so the
/// deny-by-default authorization middleware and the FluentValidation boundary are woven ahead of every
/// handler. Mirrors <see cref="ProjectEndpoints"/>. The caller is resolved from <c>ICurrentUser</c> inside
/// the handlers — never the wire.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine.Http discovers and maps HTTP endpoints only on public types; this class must stay public.")]
public static class MemberEndpoints
{
    /// <summary>
    /// List the composed members roster (owner ∪ editor/viewer rows). Readable by any current member
    /// (viewer+); a non-member → 404 (existence not disclosed). Surfaces the project <c>version</c>.
    /// </summary>
    [WolverineGet("/api/projects/{id}/members")]
    public static Task<MembersResponse> List(Guid id, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<MembersResponse>(new GetProjectMembers { ProjectId = ProjectId.From(id) });
    }

    /// <summary>
    /// Invite an admitted User by email at an assignable role. Owner-only (editor/viewer member → 403,
    /// non-member → 404). Unknown email / self / duplicate → 422; a stale <c>version</c> → 409.
    /// </summary>
    [WolverinePost("/api/projects/{id}/members")]
    public static Task<MemberResponse> Invite(Guid id, InviteMemberRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<MemberResponse>(new InviteMember
        {
            Id = ProjectId.From(id),
            Email = request.Email,
            Role = request.Role,
            Version = request.Version,
        });
    }

    /// <summary>
    /// Change a member's assignable role (editor ↔ viewer). Owner-only. Owner as target → 409
    /// <c>last_owner</c>; target who is neither owner nor member → 404; a stale <c>version</c> → 409.
    /// </summary>
    [WolverinePatch("/api/projects/{id}/members/{userId}")]
    public static Task<MemberResponse> ChangeRole(Guid id, Guid userId, ChangeMemberRoleRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<MemberResponse>(new ChangeMemberRole
        {
            Id = ProjectId.From(id),
            TargetUserId = UserId.From(userId),
            Role = request.Role,
            Version = request.Version,
        });
    }

    /// <summary>
    /// Remove a member (owner removes another user). Owner-only. Owner as target → 409 <c>last_owner</c>;
    /// target who is neither owner nor member → 404; a stale <c>version</c> → 409. The removed user loses ALL
    /// access immediately. <c>version</c> rides a query param (DELETE-body posture). Returns 204.
    /// </summary>
    [WolverineDelete("/api/projects/{id}/members/{userId}")]
    public static Task Remove(Guid id, Guid userId, int version, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync(new RemoveMember
        {
            Id = ProjectId.From(id),
            TargetUserId = UserId.From(userId),
            Version = version,
        });
    }

    /// <summary>
    /// Leave a shared project (a non-owner member removes their OWN row). Self-service. The owner cannot
    /// leave → 409 <c>last_owner</c>; a non-member → 404; a stale <c>version</c> → 409. <c>version</c> rides a
    /// query param. Returns 204.
    /// </summary>
    [WolverineDelete("/api/projects/{id}/membership")]
    public static Task Leave(Guid id, int version, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync(new LeaveProject
        {
            Id = ProjectId.From(id),
            Version = version,
        });
    }
}
