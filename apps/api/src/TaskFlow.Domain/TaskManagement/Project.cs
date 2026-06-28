using TaskFlow.Domain.Common;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement.Events;

namespace TaskFlow.Domain.TaskManagement;

/// <summary>
/// An organizational container for tasks and the second aggregate in the Task Management bounded
/// context (ENT-02). State-stored via EF Core; authorization lives in the application layer
/// (ADR-0003 Decision 6), not here.
/// </summary>
/// <remarks>
/// <para>Modeled on <see cref="Task"/>: the id is client-supplied (FR-001 parity, mapped
/// <c>ValueGeneratedNever</c>), <c>Version</c> starts at 0, and every mutating behavior method
/// stamps <see cref="UpdatedAt"/> and increments <see cref="Version"/>. <see cref="OwnerId"/> is
/// immutable (set in the ctor, never reassigned) and doubles as the authorization key.</para>
/// <para>Archive (<see cref="ArchivedAt"/>) is a REVERSIBLE lifecycle state, kept distinct from the
/// terminal soft-delete tombstone (<see cref="DeletedAt"/>) (R2). <see cref="SoftDelete"/> is
/// idempotent. <see cref="Visibility"/> defaults to <c>personal</c>; the <c>shared</c> value is
/// reserved for slice 007 (R11).</para>
/// <para>The one-level-nesting rule (FR-012, R3) is a CROSS-ROW invariant — it depends on the
/// candidate parent's own parent and on whether this project has children. The aggregate cannot
/// read those rows, so it exposes the rule as the PURE static guard
/// <see cref="EnsureNestingAllowed(ProjectId?, bool, bool)"/>; the command handler performs the
/// repository lookups and calls it before <see cref="Create"/>/<see cref="Edit"/> (R3). Likewise
/// <see cref="Unarchive(bool, DateTime)"/> receives the "parent still hidden" fact (the handler
/// resolves it) and applies the R9 orphan-to-top-level rule.</para>
/// </remarks>
public sealed class Project : AggregateRoot<ProjectId>
{
    private const int MaxNameLength = 200;

    /// <summary>The default visibility value (R11). A project starts personal.</summary>
    public const string PersonalVisibility = "personal";

    /// <summary>The shared visibility value (research R3) — made writable this slice by <see cref="Share"/>.</summary>
    public const string SharedVisibility = "shared";

    private Project()
    {
        // EF Core materialization constructor. Non-nullable values are populated from the database
        // by EF; the null-forgiving defaults satisfy the compiler.
        Name = null!;
        Color = null!;
        Icon = null!;
        Visibility = null!;
    }

    private Project(ProjectId id, UserId ownerId, string name, string color, string icon, ProjectId? parentId, DateTime utcNow)
    {
        Id = id;
        OwnerId = ownerId;
        Name = name;
        Color = color;
        Icon = icon;
        ParentId = parentId;
        Visibility = PersonalVisibility;
        Version = 0;
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
    }

    /// <summary>The owning <see cref="User"/> (R13). Immutable; the ownership/authorization anchor.</summary>
    public UserId OwnerId { get; private set; }

    /// <summary>The project name; trimmed-non-empty and ≤ 200 chars. Output-encoded on render (FR-099).</summary>
    public string Name { get; private set; }

    /// <summary>The preset color token (R10/ASM-04); preset membership is validated upstream.</summary>
    public string Color { get; private set; }

    /// <summary>The preset icon token (R10/ASM-04); preset membership is validated upstream.</summary>
    public string Icon { get; private set; }

    /// <summary>The parent project, or null for a top-level project. One-level rule (R3). FK <c>ON DELETE SET NULL</c>.</summary>
    public ProjectId? ParentId { get; private set; }

    /// <summary>Visibility (R11); defaults to <c>personal</c>. The <c>shared</c> value is slice 007.</summary>
    public string Visibility { get; private set; }

    /// <summary>Archive state (R2): null = active, non-null = archived (reversible). Distinct from <see cref="DeletedAt"/>.</summary>
    public DateTime? ArchivedAt { get; private set; }

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>Last-mutation timestamp (UTC); stamped by every behavior method.</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>Optimistic-concurrency token; incremented by every mutating behavior method.</summary>
    public int Version { get; private set; }

    /// <summary>Soft-delete tombstone (UTC); never exposed in the read model (FR-097). Terminal (R2/R5).</summary>
    public DateTime? DeletedAt { get; private set; }

    /// <summary>
    /// Creates a new project (R1). The id is client-supplied (FR-001 parity); <see cref="Visibility"/>
    /// defaults to <c>personal</c> (R11) and <see cref="Version"/> starts at 0 (creation is not a
    /// mutation). The one-level-nesting rule for <paramref name="parentId"/> is enforced UPSTREAM by
    /// the handler via <see cref="EnsureNestingAllowed(ProjectId?, bool, bool)"/> (the cross-row check
    /// needs repository lookups, R3); the aggregate sets whatever the validated command supplies.
    /// </summary>
    /// <param name="id">Client-generated identity.</param>
    /// <param name="ownerId">The owning user; immutable ownership key (R13).</param>
    /// <param name="name">The project name; trimmed-non-empty and ≤ 200 chars.</param>
    /// <param name="color">A preset color token (membership validated upstream).</param>
    /// <param name="icon">A preset icon token (membership validated upstream).</param>
    /// <param name="parentId">The top-level parent, or null for a top-level project.</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public static Project Create(ProjectId id, UserId ownerId, string name, string color, string icon, ProjectId? parentId, DateTime utcNow)
    {
        var normalizedName = NormalizeName(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(color);
        ArgumentException.ThrowIfNullOrWhiteSpace(icon);

        return new Project(id, ownerId, normalizedName, color, icon, parentId, utcNow);
    }

    /// <summary>
    /// Updates the mutable fields together (R4, whole-object replace): name, color, icon, and parent.
    /// <paramref name="parentId"/> is authoritative — null makes the project top-level, a value
    /// (re-)parents it. The one-level-nesting rule is enforced UPSTREAM by the handler via
    /// <see cref="EnsureNestingAllowed(ProjectId?, bool, bool)"/> (R3). Stamps <see cref="UpdatedAt"/>
    /// and bumps <see cref="Version"/>.
    /// </summary>
    /// <param name="name">The new name; trimmed-non-empty and ≤ 200 chars.</param>
    /// <param name="color">The new preset color token (membership validated upstream).</param>
    /// <param name="icon">The new preset icon token (membership validated upstream).</param>
    /// <param name="parentId">The new top-level parent, or null for top-level.</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public void Edit(string name, string color, string icon, ProjectId? parentId, DateTime utcNow)
    {
        var normalizedName = NormalizeName(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(color);
        ArgumentException.ThrowIfNullOrWhiteSpace(icon);

        Name = normalizedName;
        Color = color;
        Icon = icon;
        ParentId = parentId;
        Touch(utcNow);
    }

    /// <summary>
    /// The PURE one-level-nesting guard (FR-012, R3). The handler resolves the two cross-row facts
    /// (via the repository) and calls this before <see cref="Create"/>/<see cref="Edit"/>:
    /// <list type="number">
    /// <item><b>parent-is-already-a-child</b> — <paramref name="parentIsTopLevel"/> is false: setting
    /// it as parent would create a grandchild.</item>
    /// <item><b>project-has-children</b> — <paramref name="projectHasChildren"/> is true: giving this
    /// project a parent would push its own children to depth 2.</item>
    /// </list>
    /// A null <paramref name="parentId"/> (top-level) is always allowed. A violation throws
    /// <see cref="InvalidOperationException"/>, which the handler surfaces as 422 <c>validation_failed</c>
    /// on <c>parentId</c> (R12); the 404-before-422 ownership precedence (a foreign parent → 404) is
    /// resolved by the handler BEFORE this guard runs (R3/R13).
    /// </summary>
    /// <param name="parentId">The candidate parent, or null for top-level.</param>
    /// <param name="parentIsTopLevel">Whether the candidate parent is itself top-level (its own <c>ParentId</c> is null).</param>
    /// <param name="projectHasChildren">Whether the project being (re-)parented already has children.</param>
    public static void EnsureNestingAllowed(ProjectId? parentId, bool parentIsTopLevel, bool projectHasChildren)
    {
        if (parentId is null)
        {
            return;
        }

        if (!parentIsTopLevel)
        {
            throw new InvalidOperationException(
                "A project can be nested at most one level deep: the chosen parent is itself a child.");
        }

        if (projectHasChildren)
        {
            throw new InvalidOperationException(
                "A project can be nested at most one level deep: this project has its own children.");
        }
    }

    /// <summary>
    /// Archives the project (R2/FR-013): stamps <see cref="ArchivedAt"/> — a reversible state hidden
    /// from default views. Bumps <see cref="Version"/> and stamps <see cref="UpdatedAt"/>. The child
    /// disposition (AS-10) is applied by the handler to the children, not here.
    /// </summary>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public void Archive(DateTime utcNow)
    {
        ArchivedAt = utcNow;
        Touch(utcNow);
    }

    /// <summary>
    /// Unarchives the project (R2/AS-11): clears <see cref="ArchivedAt"/>. Per R9, if the project's
    /// parent is still archived or deleted (<paramref name="parentStillHidden"/> is true), the parent
    /// is nulled so the project is restored as TOP-LEVEL rather than re-nested under a hidden parent.
    /// The handler resolves the "parent still hidden" fact. Bumps <see cref="Version"/> and stamps
    /// <see cref="UpdatedAt"/>.
    /// </summary>
    /// <param name="parentStillHidden">Whether the project's current parent is still archived/deleted (R9).</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public void Unarchive(bool parentStillHidden, DateTime utcNow)
    {
        ArchivedAt = null;
        if (parentStillHidden)
        {
            ParentId = null;
        }

        Touch(utcNow);
    }

    /// <summary>
    /// Soft-deletes the project (FR-097): stamps <see cref="DeletedAt"/>. Idempotent — a second call
    /// on an already-tombstoned row is a guarded no-op (no re-stamp, no version bump), mirroring
    /// <c>Task.SoftDelete</c>. The task/child dispositions (R5) are applied by the handler BEFORE this
    /// tombstone, in the same transaction.
    /// </summary>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public void SoftDelete(DateTime utcNow)
    {
        if (DeletedAt is not null)
        {
            return;
        }

        DeletedAt = utcNow;
        Touch(utcNow);
    }

    /// <summary>
    /// Converts a personal project to <c>shared</c> (research R3, FR-058) — the <b>first legal write</b> of
    /// <see cref="SharedVisibility"/> (slice 004 froze the value at <c>personal</c>). Valid only from
    /// <c>personal</c>; a freshly shared project has zero membership rows (the owner is implicit via
    /// <see cref="OwnerId"/> — members are added by separate invites). Bumps <see cref="Version"/>, stamps
    /// <see cref="UpdatedAt"/>, and raises <see cref="ProjectShared"/> (R13).
    /// </summary>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    /// <exception cref="InvalidOperationException">The project is not currently personal.</exception>
    public void Share(DateTime utcNow)
    {
        if (Visibility != PersonalVisibility)
        {
            throw new InvalidOperationException("Only a personal project can be shared.");
        }

        Visibility = SharedVisibility;
        Touch(utcNow);
        AddDomainEvent(new ProjectShared(Id, OwnerId));
    }

    /// <summary>
    /// Re-personalizes a shared project (research R3, FR-058): flips <see cref="Visibility"/> back to
    /// <c>personal</c>. The handler removes ALL membership rows in the same transaction (R3) — this method
    /// only flips the project's own state; <see cref="OwnerId"/> and the project's tasks are retained. Bumps
    /// <see cref="Version"/>, stamps <see cref="UpdatedAt"/>, and raises <see cref="ProjectUnshared"/> (R13).
    /// </summary>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    /// <exception cref="InvalidOperationException">The project is not currently shared.</exception>
    public void Unshare(DateTime utcNow)
    {
        if (Visibility != SharedVisibility)
        {
            throw new InvalidOperationException("Only a shared project can be unshared.");
        }

        Visibility = PersonalVisibility;
        Touch(utcNow);
        AddDomainEvent(new ProjectUnshared(Id, OwnerId));
    }

    /// <summary>
    /// Reassigns <see cref="OwnerId"/> to <paramref name="newOwner"/> (research R6, FR-094) — the <b>only</b>
    /// legal mutation of the otherwise-immutable owner. Valid only on a shared project. The handler performs
    /// the surrounding membership reconciliation (remove the new owner's row, insert an <c>editor</c> row for
    /// the prior owner) in the same transaction; this method only moves the anchor. Bumps
    /// <see cref="Version"/>, stamps <see cref="UpdatedAt"/>, and raises <see cref="OwnerTransferred"/> (R13).
    /// </summary>
    /// <param name="newOwner">The current member who becomes the new owner.</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    /// <exception cref="InvalidOperationException">The project is not shared, or the target is already the owner.</exception>
    public void TransferOwnerTo(UserId newOwner, DateTime utcNow)
    {
        if (Visibility != SharedVisibility)
        {
            throw new InvalidOperationException("Ownership can only be transferred on a shared project.");
        }

        if (newOwner == OwnerId)
        {
            throw new InvalidOperationException("The target is already the owner.");
        }

        var priorOwner = OwnerId;
        OwnerId = newOwner;
        Touch(utcNow);
        AddDomainEvent(new OwnerTransferred(Id, priorOwner, newOwner));
    }

    /// <summary>
    /// Advances the optimistic-concurrency token for a membership-set mutation (invite / change-role /
    /// remove / leave) that changes the project's sharing state without altering a <see cref="Project"/>
    /// field. The single <see cref="Version"/> token guards the whole sharing state (R1/R11), so every
    /// membership command bumps it; the membership-row mutation and any <c>MembershipRevoked</c> event are
    /// the handler's responsibility. Stamps <see cref="UpdatedAt"/> and bumps <see cref="Version"/>.
    /// </summary>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public void RecordMembershipChange(DateTime utcNow) => Touch(utcNow);

    /// <summary>
    /// The PURE last-owner guard (research R7, mirroring <see cref="EnsureNestingAllowed"/>). Under the
    /// single-immutable-owner model the "last owner" degenerates to "the owner", so any leave / remove /
    /// demote whose <paramref name="target"/> equals the project's <see cref="OwnerId"/> is rejected with a
    /// recoverable <see cref="LastOwnerException"/> (→ 409 <c>last_owner</c>). Called by the handler
    /// <b>before</b> the membership-row lookup (the owner has no row), so the owner-as-target case yields the
    /// actionable "transfer first" message instead of a misleading 404. A no-op for any other target.
    /// </summary>
    /// <param name="project">The project whose ownership anchor is checked.</param>
    /// <param name="target">The user the operation targets.</param>
    /// <exception cref="LastOwnerException"><paramref name="target"/> is the project's owner.</exception>
    public static void EnsureNotLastOwner(Project project, UserId target)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (target == project.OwnerId)
        {
            throw new LastOwnerException();
        }
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
        {
            throw new ArgumentException($"Name must be {MaxNameLength} characters or fewer.", nameof(name));
        }

        return trimmed;
    }

    private void Touch(DateTime utcNow)
    {
        UpdatedAt = utcNow;
        Version++;
    }
}
