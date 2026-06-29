using TaskFlow.Domain.Common;
using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Domain.TaskManagement;

/// <summary>
/// A reusable per-user tag (ENT-04) for cross-cutting categorization, applied to many tasks via the
/// many-to-many <c>task_labels</c> relation. The third aggregate in the Task Management bounded context.
/// State-stored via EF Core; authorization lives in the application layer (ADR-0003), not here.
/// </summary>
/// <remarks>
/// <para>The label is <b>per-user</b> (Tier A): <see cref="OwnerId"/> is the immutable ownership key
/// (FR-065). The id is client-supplied (FR-001 parity, mapped <c>ValueGeneratedNever</c>).</para>
/// <para>There is intentionally <b>no optimistic <c>Version</c></b>: a label is edited only by its single
/// owner, so there is no concurrent-editor conflict to guard (contrast <see cref="Task"/>/<see cref="Project"/>).
/// Applying a label to a task is a per-user relation mutation handled outside this aggregate (data-model R2),
/// so it does not touch the label either.</para>
/// <para>Per-owner case-insensitive uniqueness of <see cref="Name"/> is a CROSS-ROW invariant enforced by the
/// handler (a normalized-name pre-check) + a DB unique index on <c>(owner_id, name_normalized)</c>; the
/// aggregate maintains <see cref="NameNormalized"/> so both sides agree (data-model R7).</para>
/// </remarks>
public sealed class Label : AggregateRoot<LabelId>
{
    private const int MaxNameLength = 50;

    private Label()
    {
        // EF Core materialization constructor. Non-nullable values are populated from the database by EF;
        // the null-forgiving defaults satisfy the compiler.
        Name = null!;
        NameNormalized = null!;
    }

    private Label(LabelId id, UserId ownerId, string name, string nameNormalized, string? color, DateTime utcNow)
    {
        Id = id;
        OwnerId = ownerId;
        Name = name;
        NameNormalized = nameNormalized;
        Color = color;
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
    }

    /// <summary>The owning <see cref="User"/> (FR-065 Tier A). Immutable; the ownership/authorization anchor.</summary>
    public UserId OwnerId { get; private set; }

    /// <summary>The display name; trimmed-non-empty and ≤ 50 chars. Untrusted content — output-encoded on render (FR-099).</summary>
    public string Name { get; private set; }

    /// <summary>
    /// The case-folded name (<see cref="Name"/> folded with <see cref="string.ToUpperInvariant"/> — CA1308:
    /// upper-case is the round-trip-safe fold), backing the per-owner case-insensitive uniqueness via a plain
    /// unique index on <c>(owner_id, name_normalized)</c> (data-model R7 — EF Core 9 cannot model a functional
    /// expression index). Kept in lock-step with <see cref="Name"/> by <see cref="Create"/>/<see cref="Edit"/>.
    /// Never exposed in the read model.
    /// </summary>
    public string NameNormalized { get; private set; }

    /// <summary>The optional preset color token (ASM-04, R7); preset membership is validated upstream. Decorative only (FR-044).</summary>
    public string? Color { get; private set; }

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>Last-mutation timestamp (UTC); stamped by <see cref="Create"/> and <see cref="Edit"/>.</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a new label owned by <paramref name="ownerId"/> (R1/R3). The id is client-supplied; the name is
    /// trimmed/validated and the normalized form is derived. Uniqueness is a cross-row rule enforced UPSTREAM by
    /// the handler + the DB index (not here). <paramref name="color"/> preset membership is validated upstream.
    /// </summary>
    /// <param name="id">Client-generated identity.</param>
    /// <param name="ownerId">The owning user; immutable ownership key (FR-065).</param>
    /// <param name="name">The label name; trimmed-non-empty and ≤ 50 chars.</param>
    /// <param name="color">An optional preset color token (membership validated upstream), or null.</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public static Label Create(LabelId id, UserId ownerId, string name, string? color, DateTime utcNow)
    {
        var normalizedName = NormalizeName(name);
        return new Label(id, ownerId, normalizedName, NormalizeForUniqueness(normalizedName), color, utcNow);
    }

    /// <summary>
    /// The case-fold key used for per-owner uniqueness — the trimmed name folded with
    /// <see cref="string.ToUpperInvariant"/> (CA1308: upper-case is the round-trip-safe fold). Used by the
    /// aggregate AND the create/update handlers' duplicate pre-check, so both agree with the
    /// <c>(owner_id, name_normalized)</c> unique index (R7). The stored value is opaque (never displayed).
    /// </summary>
    public static string NormalizeForUniqueness(string name) => NormalizeName(name).ToUpperInvariant();

    /// <summary>
    /// Whole-object replace of the mutable fields (R3) — realizes both rename and recolor. Re-normalizes the
    /// name (and <see cref="NameNormalized"/>), sets <paramref name="color"/>, and stamps <see cref="UpdatedAt"/>.
    /// Uniqueness is enforced upstream; <paramref name="color"/> preset membership is validated upstream.
    /// </summary>
    /// <param name="name">The new name; trimmed-non-empty and ≤ 50 chars.</param>
    /// <param name="color">The new optional preset color token, or null to clear.</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public void Edit(string name, string? color, DateTime utcNow)
    {
        var normalizedName = NormalizeName(name);
        Name = normalizedName;
        NameNormalized = NormalizeForUniqueness(normalizedName);
        Color = color;
        UpdatedAt = utcNow;
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
}
