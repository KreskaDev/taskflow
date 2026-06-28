using TaskFlow.Application.Authorization;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// A single entry in a shared project's members roster (contracts/openapi.yaml <c>MemberResponse</c>,
/// research R17). Surfaces <c>displayName</c> + the effective <c>role</c> — <b>never the member's email</b>
/// (Constitution XI privacy: invite is <i>by</i> email, but the roster need not expose addresses).
/// </summary>
public sealed record MemberResponse
{
    /// <summary>The member's User id.</summary>
    public required Guid UserId { get; init; }

    /// <summary>The member's display name (output-encoded on render, FR-099). NOT their email.</summary>
    public required string DisplayName { get; init; }

    /// <summary>The effective role token: <c>owner</c> for the owner entry, else <c>editor</c>/<c>viewer</c>.</summary>
    public required string Role { get; init; }

    /// <summary>True for the owner entry (derived from <c>ownerId</c>; the owner has no membership row).</summary>
    public required bool IsOwner { get; init; }

    /// <summary>Builds an entry from a resolved User id, display name, and effective role.</summary>
    public static MemberResponse From(Guid userId, string displayName, EffectiveRole role) => new()
    {
        UserId = userId,
        DisplayName = displayName,
        Role = role.ToWireValue() ?? throw new ArgumentOutOfRangeException(nameof(role), role, "A roster entry must have an effective role."),
        IsOwner = role == EffectiveRole.Owner,
    };
}

/// <summary>
/// The members-roster response (contracts/openapi.yaml <c>MembersResponse</c>): the composed roster plus
/// the project <c>version</c> so <c>leave</c>/<c>change-role</c>/<c>remove</c>/<c>transfer</c> callers carry
/// the concurrency token without a separate fetch (research R11/R17).
/// </summary>
public sealed record MembersResponse
{
    /// <summary>The project the roster belongs to.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>The Project optimistic-concurrency token (the token for membership mutations, R11).</summary>
    public required int Version { get; init; }

    /// <summary>The owner entry (<c>isOwner=true</c>) ∪ the editor/viewer rows.</summary>
    public required IReadOnlyList<MemberResponse> Members { get; init; }
}
