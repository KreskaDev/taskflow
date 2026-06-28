using TaskFlow.Domain.TaskManagement;

namespace TaskFlow.Application.Authorization;

/// <summary>
/// The composed, read-time role a caller holds on a project (research R2/R17). Distinct from the
/// stored <see cref="MembershipRoles"/> vocabulary (<c>editor | viewer</c>): <see cref="Owner"/> is
/// <b>derived</b> from <c>Project.OwnerId == caller</c> (never a stored row), and <see cref="None"/>
/// denotes a non-member. The integer values are <b>rank-ordered</b> so a "≥ required role" check is a
/// simple comparison (<c>Viewer &lt; Editor &lt; Owner</c>).
/// </summary>
public enum EffectiveRole
{
    /// <summary>Not a member and not the owner — a non-member (resolves to a 404, never disclosed).</summary>
    None = 0,

    /// <summary>Read-only member.</summary>
    Viewer = 1,

    /// <summary>Read + write member.</summary>
    Editor = 2,

    /// <summary>The project owner (the immutable <c>OwnerId</c>); the manage tier.</summary>
    Owner = 3,
}

/// <summary>Wire-serialization helpers for <see cref="EffectiveRole"/> (the contract's lowercase tokens).</summary>
public static class EffectiveRoleExtensions
{
    /// <summary>
    /// The lowercase wire token for an effective role (<c>owner | editor | viewer</c>), or <c>null</c> for
    /// <see cref="EffectiveRole.None"/> (a non-member is never surfaced as a role value — research R17).
    /// </summary>
    public static string? ToWireValue(this EffectiveRole role) => role switch
    {
        EffectiveRole.Owner => "owner",
        EffectiveRole.Editor => MembershipRoles.Editor,
        EffectiveRole.Viewer => MembershipRoles.Viewer,
        _ => null,
    };
}
