using TaskFlow.Domain.Common;
using TaskFlow.Domain.IdentityAccess;

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

    /// <summary>The default (and, this slice, only writable) visibility value (R11).</summary>
    public const string PersonalVisibility = "personal";

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
