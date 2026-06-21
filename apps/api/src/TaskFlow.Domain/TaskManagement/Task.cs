using TaskFlow.Domain.Common;
using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Domain.TaskManagement;

/// <summary>
/// The core work item and first aggregate in the Task Management bounded context
/// (ENT-01). State-stored via EF Core; authorization lives in the application layer
/// (ADR-0003 Decision 6), not here.
/// </summary>
/// <remarks>
/// <para>The id and <c>position</c> are client-supplied (FR-001); <c>status</c> defaults
/// to <see cref="TaskStatus.Backlog"/> and <c>version</c> starts at 0. Every mutating
/// behavior method stamps <see cref="UpdatedAt"/> and increments <see cref="Version"/>.</para>
/// <para><see cref="CreatedBy"/> is immutable (set in the ctor, never reassigned).
/// <see cref="CompletedAt"/> is set iff <c>status = done</c>. <see cref="SoftDelete"/>
/// is idempotent.</para>
/// <para>The seven reserved nullable columns (<c>Description</c>, <c>Priority</c>,
/// <c>DueDate</c>, <c>DueHasTime</c>, <c>ProjectId</c>, <c>CycleId</c>,
/// <c>RecurrenceRule</c>) are mapped but unused this slice — populated by their owning
/// slices so no later migration is needed.</para>
/// </remarks>
public sealed class Task : AggregateRoot<TaskId>
{
    private const int MaxTitleLength = 500;

    private Task()
    {
        // EF Core materialization constructor. Non-nullable values are populated
        // from the database by EF; the null-forgiving defaults satisfy the compiler.
        Title = null!;
        Position = null!;
    }

    private Task(TaskId id, UserId createdBy, string title, string position, DateTime utcNow)
    {
        Id = id;
        CreatedBy = createdBy;
        Title = title;
        Position = position;
        Status = TaskStatus.Backlog;
        Version = 0;
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
    }

    /// <summary>The creating <see cref="User"/> (FR-002). Immutable; doubles as the ownership key.</summary>
    public UserId CreatedBy { get; private set; }

    /// <summary>The task title; trimmed-non-empty and ≤ 500 chars (FR-001).</summary>
    public string Title { get; private set; }

    /// <summary>Lifecycle status (FR-003); defaults to <see cref="TaskStatus.Backlog"/>.</summary>
    public TaskStatus Status { get; private set; }

    /// <summary>Lexicographic fractional rank string; client-authoritative on create (FR-102).</summary>
    public string Position { get; private set; }

    /// <summary>Optimistic-concurrency token; incremented by every mutating behavior method.</summary>
    public int Version { get; private set; }

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>Last-mutation timestamp (UTC); stamped by every behavior method.</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>Completion timestamp (UTC). Set iff <see cref="Status"/> is <see cref="TaskStatus.Done"/>.</summary>
    public DateTime? CompletedAt { get; private set; }

    /// <summary>Soft-delete tombstone (UTC); never exposed in the read model (FR-097).</summary>
    public DateTime? DeletedAt { get; private set; }

    /// <summary>Reserved (slice 005) — full editor description. Mapped but unused this slice.</summary>
    public string? Description { get; private set; }

    /// <summary>Reserved (slice 005) — priority P0–P3. Mapped but unused this slice.</summary>
    public string? Priority { get; private set; }

    /// <summary>Reserved (slice 003/005) — due date. Mapped but unused this slice.</summary>
    public DateTime? DueDate { get; private set; }

    /// <summary>Reserved (slice 003/005) — the <c>DueDate.has_time</c> flag. Mapped but unused this slice.</summary>
    public bool? DueHasTime { get; private set; }

    /// <summary>Reserved (slice 004) — owning project. Mapped but unused this slice.</summary>
    public Guid? ProjectId { get; private set; }

    /// <summary>Reserved (slice 011) — owning cycle. Mapped but unused this slice.</summary>
    public Guid? CycleId { get; private set; }

    /// <summary>Reserved (slice 012) — recurrence rule (jsonb). Mapped but unused this slice.</summary>
    public string? RecurrenceRule { get; private set; }

    /// <summary>Creates a new task. The id and position are client-supplied (FR-001).</summary>
    /// <param name="id">Client-generated identity.</param>
    /// <param name="createdBy">The creating user; immutable ownership key (FR-002).</param>
    /// <param name="title">The task title; trimmed-non-empty and ≤ 500 chars.</param>
    /// <param name="position">Client-authoritative fractional rank string.</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public static Task Create(TaskId id, UserId createdBy, string title, string position, DateTime utcNow)
    {
        var normalizedTitle = NormalizeTitle(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(position);

        return new Task(id, createdBy, normalizedTitle, position, utcNow);
    }

    /// <summary>Replaces the title (FR-001) with a trimmed-non-empty, ≤ 500 char value.</summary>
    /// <param name="title">The new title.</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public void Rename(string title, DateTime utcNow)
    {
        Title = NormalizeTitle(title);
        Touch(utcNow);
    }

    /// <summary>Marks the task done (FR-003): sets <see cref="Status"/> and stamps <see cref="CompletedAt"/>.</summary>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public void MarkDone(DateTime utcNow)
    {
        Status = TaskStatus.Done;
        CompletedAt = utcNow;
        Touch(utcNow);
    }

    /// <summary>Un-completes the task (FR-003): sets <see cref="Status"/> to backlog and clears <see cref="CompletedAt"/>.</summary>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public void MarkBacklog(DateTime utcNow)
    {
        Status = TaskStatus.Backlog;
        CompletedAt = null;
        Touch(utcNow);
    }

    /// <summary>Moves the task to a new fractional rank (FR-102).</summary>
    /// <param name="position">The new fractional rank string.</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public void Reorder(string position, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(position);

        Position = position;
        Touch(utcNow);
    }

    /// <summary>
    /// Soft-deletes the task (FR-097): stamps <see cref="DeletedAt"/>. Idempotent — a second
    /// call on an already-tombstoned row is a guarded no-op (no re-stamp, no version bump).
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

    private static string NormalizeTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var trimmed = title.Trim();
        if (trimmed.Length > MaxTitleLength)
        {
            throw new ArgumentException($"Title must be {MaxTitleLength} characters or fewer.", nameof(title));
        }

        return trimmed;
    }

    private void Touch(DateTime utcNow)
    {
        UpdatedAt = utcNow;
        Version++;
    }
}
