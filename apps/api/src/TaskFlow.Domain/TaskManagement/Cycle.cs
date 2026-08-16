using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.TaskManagement;

/// <summary>
/// A time-boxed period for organizing work (ENT-03, slice 011 D1) — the team-wide sprint. The
/// fourth aggregate in the Task Management bounded context. State-stored via EF Core;
/// authorization lives in the application layer (ADR-0003), not here.
/// </summary>
/// <remarks>
/// <para>The cycle is <b>team-wide</b> (ASM-10): it has NO owner column — lifecycle operations are
/// explicit deny-by-default authorized commands (spec IX), not ownership-gated ones.</para>
/// <para><see cref="StartDate"/>/<see cref="EndDate"/> are UTC instants (Warsaw-midnight date-only
/// convention, D18); <c>start &lt; end</c> is the ONLY cross-field rule — ranges of different
/// cycles may overlap (Clarifications 2026-08-16). The single-active invariant rides on
/// <see cref="Status"/>, enforced by the <see cref="Activate"/> guard, the handler check, and the
/// <c>ix_cycles_single_active</c> partial unique index (D3).</para>
/// <para>Every mutating behavior method stamps <see cref="UpdatedAt"/> and increments
/// <see cref="Version"/> (OCC, the Task/Project convention).</para>
/// </remarks>
public sealed class Cycle : AggregateRoot<CycleId>
{
    private const int MaxNameLength = 200;

    private Cycle()
    {
        // EF Core materialization constructor. Non-nullable values are populated
        // from the database by EF; the null-forgiving defaults satisfy the compiler.
        Name = null!;
    }

    private Cycle(CycleId id, string name, DateTime startDate, DateTime endDate, DateTime utcNow)
    {
        Id = id;
        Name = name;
        StartDate = startDate;
        EndDate = endDate;
        Status = CycleStatus.Planned;
        Version = 0;
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
    }

    /// <summary>The display name; trimmed-non-empty and ≤ 200 chars. Untrusted content — output-encoded on render (FR-099).</summary>
    public string Name { get; private set; }

    /// <summary>Start instant (UTC, Warsaw-midnight date-only convention). Invariant: strictly before <see cref="EndDate"/>.</summary>
    public DateTime StartDate { get; private set; }

    /// <summary>End instant (UTC). Passing it does NOT auto-close the cycle — the UI marks it overdue instead.</summary>
    public DateTime EndDate { get; private set; }

    /// <summary>Lifecycle status; a fresh cycle is <see cref="CycleStatus.Planned"/>.</summary>
    public CycleStatus Status { get; private set; }

    /// <summary>Optimistic-concurrency token; incremented by every mutating behavior method.</summary>
    public int Version { get; private set; }

    /// <summary>Creation timestamp (UTC) — the D5 ordering tiebreaker for equal start dates.</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>Last-mutation timestamp (UTC); stamped by every behavior method.</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a new planned cycle. The id is client-supplied (idempotent PUT); creation is not a
    /// mutation, so <see cref="Version"/> stays 0.
    /// </summary>
    /// <param name="id">Client-generated identity.</param>
    /// <param name="name">The cycle name; trimmed-non-empty and ≤ 200 chars.</param>
    /// <param name="startDate">Start instant (UTC); must be strictly before <paramref name="endDate"/>.</param>
    /// <param name="endDate">End instant (UTC).</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public static Cycle Create(CycleId id, string name, DateTime startDate, DateTime endDate, DateTime utcNow)
    {
        EnsureDateOrder(startDate, endDate);
        return new Cycle(id, NormalizeName(name), startDate, endDate, utcNow);
    }

    /// <summary>Replaces the name (FR-020) — legal in EVERY status.</summary>
    /// <param name="name">The new name; trimmed-non-empty and ≤ 200 chars.</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public void Rename(string name, DateTime utcNow)
    {
        Name = NormalizeName(name);
        Touch(utcNow);
    }

    /// <summary>Replaces the date range (FR-020) — legal in EVERY status; <c>start &lt; end</c> re-validated.</summary>
    /// <param name="startDate">The new start instant (UTC).</param>
    /// <param name="endDate">The new end instant (UTC).</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public void Reschedule(DateTime startDate, DateTime endDate, DateTime utcNow)
    {
        EnsureDateOrder(startDate, endDate);
        StartDate = startDate;
        EndDate = endDate;
        Touch(utcNow);
    }

    /// <summary>
    /// The manual planned → active transition. Legal ONLY from <see cref="CycleStatus.Planned"/>;
    /// the handler maps the guard to 422 <c>cycle_not_planned</c> and owns the single-active
    /// check (409 <c>cycle_active_conflict</c>; the partial unique index wins races — D3).
    /// </summary>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    /// <exception cref="InvalidOperationException">The cycle is not planned.</exception>
    public void Activate(DateTime utcNow)
    {
        if (Status != CycleStatus.Planned)
        {
            throw new InvalidOperationException("Only a planned cycle can be activated.");
        }

        Status = CycleStatus.Active;
        Touch(utcNow);
    }

    /// <summary>
    /// The manual active → closed transition (the review commit, D4). Legal ONLY from
    /// <see cref="CycleStatus.Active"/>; the handler maps the guard to 422 <c>cycle_not_active</c>
    /// and applies the rollover to the cycle's tasks in the SAME transaction.
    /// </summary>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    /// <exception cref="InvalidOperationException">The cycle is not active.</exception>
    public void Close(DateTime utcNow)
    {
        if (Status != CycleStatus.Active)
        {
            throw new InvalidOperationException("Only an active cycle can be closed.");
        }

        Status = CycleStatus.Closed;
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

    private static void EnsureDateOrder(DateTime startDate, DateTime endDate)
    {
        if (startDate >= endDate)
        {
            throw new ArgumentException("The start date must be strictly before the end date.", nameof(startDate));
        }
    }

    private void Touch(DateTime utcNow)
    {
        UpdatedAt = utcNow;
        Version++;
    }
}
