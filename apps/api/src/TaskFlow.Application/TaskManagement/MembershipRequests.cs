using System.Diagnostics.CodeAnalysis;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The wire body for <c>POST /api/projects/{id}/members</c> (contracts/openapi.yaml
/// <c>InviteMemberRequest</c>). The project <c>id</c> is the route; the caller (owner) is resolved from
/// <c>ICurrentUser</c>. The <see cref="Email"/> is resolved SERVER-SIDE against admitted Users (R4); the
/// assignable <see cref="Role"/> is <c>editor|viewer</c> only (<c>owner</c> is unrepresentable, R2).
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record InviteMemberRequest
{
    /// <summary>The invitee's email — resolved against existing admitted Users (unknown → 422; OOS-18).</summary>
    public required string Email { get; init; }

    /// <summary>The assignable role for the new member (<c>editor</c> or <c>viewer</c>).</summary>
    public required string Role { get; init; }

    /// <summary>The Project optimistic-concurrency token; a stale value → 409.</summary>
    public required int Version { get; init; }
}

/// <summary>
/// The wire body for <c>PATCH /api/projects/{id}/members/{userId}</c> (contracts/openapi.yaml
/// <c>ChangeMemberRoleRequest</c>). Re-sending the current role is a no-op + version bump, not an error (R5).
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record ChangeMemberRoleRequest
{
    /// <summary>The member's new assignable role (<c>editor</c> or <c>viewer</c>).</summary>
    public required string Role { get; init; }

    /// <summary>The Project optimistic-concurrency token; a stale value → 409.</summary>
    public required int Version { get; init; }
}

/// <summary>
/// The wire body for <c>PATCH /api/projects/{id}/owner</c> (contracts/openapi.yaml
/// <c>TransferOwnershipRequest</c>). The target MUST already be a current member; a non-member or the
/// current owner → 422 (R6).
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http as the request body and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record TransferOwnershipRequest
{
    /// <summary>The current member to make the new owner.</summary>
    public required Guid UserId { get; init; }

    /// <summary>The Project optimistic-concurrency token; a stale value → 409.</summary>
    public required int Version { get; init; }
}
