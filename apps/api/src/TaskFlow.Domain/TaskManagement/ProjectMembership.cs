using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Domain.TaskManagement;

/// <summary>
/// Links an admitted <see cref="User"/> to a <b>shared</b> <see cref="Project"/> at a freely-assignable
/// role (ENT-07, data-model.md §1). Logically <b>owned by the <see cref="Project"/> aggregate</b>
/// (ADR-0003: the membership set is that project's sharing state and changes transactionally with the
/// project) but physically a separate <c>project_memberships</c> table loaded by a repository — there is
/// <b>no EF navigation collection</b> on <see cref="Project"/> (the slice-004 no-nav-prop style, R1).
/// </summary>
/// <remarks>
/// <para>A near-anemic data holder: it carries <b>no concurrency token of its own</b> — the owning
/// <see cref="Project"/>'s <c>Version</c> guards the whole sharing state (R1/R11). The owner is
/// <b>never</b> a membership row (ownership is <see cref="Project.OwnerId"/>, R2), so
/// <see cref="Role"/> is exactly <c>editor</c> or <c>viewer</c> (<see cref="MembershipRoles"/>).</para>
/// <para>Created server-side by the invite / transfer handlers; the role is toggled by
/// <c>ChangeMemberRole</c>; the row is removed by remove / leave / unshare / transfer. Memberships exist
/// only while the project is shared (R3).</para>
/// </remarks>
public sealed class ProjectMembership
{
    private ProjectMembership()
    {
        // EF Core materialization constructor; non-nullable values are populated from the database.
        Role = null!;
    }

    private ProjectMembership(ProjectMembershipId id, ProjectId projectId, UserId userId, string role, DateTime utcNow)
    {
        Id = id;
        ProjectId = projectId;
        UserId = userId;
        Role = role;
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
    }

    /// <summary>Surrogate identity (client/application-generated UUIDv7; <c>ValueGeneratedNever</c>).</summary>
    public ProjectMembershipId Id { get; private set; }

    /// <summary>The shared project this membership belongs to. FK → <c>projects(id)</c> ON DELETE CASCADE.</summary>
    public ProjectId ProjectId { get; private set; }

    /// <summary>The member User. FK → <c>users(id)</c> ON DELETE CASCADE (erasure parity, Constitution XI).</summary>
    public UserId UserId { get; private set; }

    /// <summary>The stored role — exactly <c>editor</c> or <c>viewer</c> (R2); never <c>owner</c>.</summary>
    public string Role { get; private set; }

    /// <summary>When the member was invited/added (UTC).</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>Last role change (UTC).</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a membership row for <paramref name="userId"/> on <paramref name="projectId"/> at
    /// <paramref name="role"/> (which MUST be <c>editor</c> or <c>viewer</c> — guarded here as the last
    /// line of defence behind the validator and the EF CHECK).
    /// </summary>
    public static ProjectMembership Create(ProjectMembershipId id, ProjectId projectId, UserId userId, string role, DateTime utcNow)
    {
        if (!MembershipRoles.IsAssignable(role))
        {
            throw new ArgumentException($"Role must be '{MembershipRoles.Editor}' or '{MembershipRoles.Viewer}'.", nameof(role));
        }

        return new ProjectMembership(id, projectId, userId, role, utcNow);
    }

    /// <summary>
    /// Sets the row's <see cref="Role"/> to <paramref name="role"/> (<c>editor</c> or <c>viewer</c>),
    /// stamping <see cref="UpdatedAt"/>. Re-setting the current value is a benign no-op assignment (the
    /// handler still bumps the Project version — research R5). Whether this is a demotion (→ a
    /// <c>MembershipRevoked</c> event) is decided by the handler from the prior value.
    /// </summary>
    public void ChangeRole(string role, DateTime utcNow)
    {
        if (!MembershipRoles.IsAssignable(role))
        {
            throw new ArgumentException($"Role must be '{MembershipRoles.Editor}' or '{MembershipRoles.Viewer}'.", nameof(role));
        }

        Role = role;
        UpdatedAt = utcNow;
    }
}
