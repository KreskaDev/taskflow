namespace TaskFlow.Domain.TaskManagement;

/// <summary>
/// The authoritative <b>stored / writable</b> membership-role vocabulary (research R2): exactly
/// <c>editor</c> or <c>viewer</c>. The owner is <b>never</b> a stored row — ownership is
/// <see cref="Project.OwnerId"/> — so <c>owner</c> is intentionally absent from this set, making the
/// illegal "promote to owner" state unrepresentable in every invite/change-role payload (Constitution VI).
/// </summary>
/// <remarks>
/// This is the single source of truth the FluentValidation role rule and the EF <c>role</c> CHECK both
/// reference (and which the web <c>membership.ts</c> source enum mirrors). The composed read-time
/// <c>owner | editor | viewer</c> vocabulary is the separate <c>EffectiveRole</c> (application layer).
/// </remarks>
public static class MembershipRoles
{
    /// <summary>Read + write capability on the shared project's tasks (the higher assignable role).</summary>
    public const string Editor = "editor";

    /// <summary>Read-only capability on the shared project (the lower assignable role).</summary>
    public const string Viewer = "viewer";

    /// <summary>True iff <paramref name="role"/> is one of the two assignable stored roles.</summary>
    public static bool IsAssignable(string? role) => role is Editor or Viewer;
}
