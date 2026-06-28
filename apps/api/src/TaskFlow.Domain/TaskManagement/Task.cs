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
/// <para><see cref="DueDate"/> and <see cref="DueHasTime"/> are written at create time
/// this slice (slice 003). The remaining reserved nullable columns (<c>Description</c>,
/// <c>Priority</c>, <c>ProjectId</c>, <c>CycleId</c>, <c>RecurrenceRule</c>) are mapped
/// but unused this slice — populated by their owning slices so no later migration is
/// needed.</para>
/// </remarks>
public sealed class Task : AggregateRoot<TaskId>
{
    private const int MaxTitleLength = 500;
    private const int MaxDescriptionLength = 8000;

    private Task()
    {
        // EF Core materialization constructor. Non-nullable values are populated
        // from the database by EF; the null-forgiving defaults satisfy the compiler.
        Title = null!;
        Position = null!;
    }

    private Task(TaskId id, UserId createdBy, string title, string position, DateTime utcNow, DateTime? dueDate, bool? dueHasTime)
    {
        Id = id;
        CreatedBy = createdBy;
        Title = title;
        Position = position;
        Status = TaskStatus.Backlog;
        Version = 0;
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
        DueDate = dueDate;
        DueHasTime = dueHasTime;
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

    /// <summary>Due-date UTC instant (slice 003). Written at create time by <see cref="Create(TaskId, UserId, string, string, DateTime, DateTime?, bool?)"/>; null for no due date.</summary>
    public DateTime? DueDate { get; private set; }

    /// <summary>The <c>DueDate.has_time</c> flag (slice 003). Written at create time by <see cref="Create(TaskId, UserId, string, string, DateTime, DateTime?, bool?)"/>; null for no due date.</summary>
    public bool? DueHasTime { get; private set; }

    /// <summary>
    /// Owning project (slice 004), or null for the Inbox (FR-021/R6). Strongly-typed to
    /// <see cref="ProjectId"/> so the <c>project_id → projects(id)</c> FK matches by CLR type
    /// (mirrors <see cref="CreatedBy"/>); the store column stays <c>uuid</c> NULL — activating the
    /// CLR type does not alter the column. Written by <c>MoveTaskToProject</c> (slice 004 T032,
    /// out of this Foundational scope); read by the Inbox/project-task queries.
    /// </summary>
    public ProjectId? ProjectId { get; private set; }

    /// <summary>Reserved (slice 011) — owning cycle. Mapped but unused this slice.</summary>
    public Guid? CycleId { get; private set; }

    /// <summary>Reserved (slice 012) — recurrence rule (jsonb). Mapped but unused this slice.</summary>
    public string? RecurrenceRule { get; private set; }

    /// <summary>Creates a new task with no due date. The id and position are client-supplied (FR-001).</summary>
    /// <param name="id">Client-generated identity.</param>
    /// <param name="createdBy">The creating user; immutable ownership key (FR-002).</param>
    /// <param name="title">The task title; trimmed-non-empty and ≤ 500 chars.</param>
    /// <param name="position">Client-authoritative fractional rank string.</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public static Task Create(TaskId id, UserId createdBy, string title, string position, DateTime utcNow)
        => Create(id, createdBy, title, position, utcNow, dueDate: null, dueHasTime: null);

    /// <summary>
    /// Creates a new task, optionally with a resolved due date. The id and position are
    /// client-supplied (FR-001). Creation is not a mutation, so <see cref="Version"/> stays 0.
    /// </summary>
    /// <param name="id">Client-generated identity.</param>
    /// <param name="createdBy">The creating user; immutable ownership key (FR-002).</param>
    /// <param name="title">The task title; trimmed-non-empty and ≤ 500 chars.</param>
    /// <param name="position">Client-authoritative fractional rank string.</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    /// <param name="dueDate">The resolved due-date UTC instant, or null for no due date.</param>
    /// <param name="dueHasTime">The <c>DueDate.has_time</c> flag, or null for no due date.
    /// The pairing invariant (both set or both null) is enforced upstream by the
    /// <c>CreateTaskValidator</c> (T007, per R11/R8); the aggregate sets whatever the
    /// validated command supplies.</param>
    public static Task Create(TaskId id, UserId createdBy, string title, string position, DateTime utcNow, DateTime? dueDate, bool? dueHasTime)
    {
        var normalizedTitle = NormalizeTitle(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(position);

        return new Task(id, createdBy, normalizedTitle, position, utcNow, dueDate, dueHasTime);
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
    /// Moves the task to <paramref name="projectId"/>, or to the Inbox when null (slice 004 US2,
    /// US-08.AS-05/R7). Assigning a project removes the task from the Inbox (FR-021); a null target
    /// clears the project and returns it to the Inbox (the natural inverse). Bumps <see cref="Version"/>
    /// and stamps <see cref="UpdatedAt"/>. The handler (<c>MoveTaskToProject</c>) authorizes ownership of
    /// BOTH the task and the target project before calling this; the aggregate just records the move.
    /// </summary>
    /// <param name="projectId">The target project, or null for the Inbox.</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public void MoveToProject(ProjectId? projectId, DateTime utcNow)
    {
        ProjectId = projectId;
        Touch(utcNow);
    }

    /// <summary>
    /// Sets the task priority to a closed-set token <c>P0</c>–<c>P3</c>, or clears it with null (slice 005,
    /// AS-04/R2). The <c>1</c>-<c>4</c> instant mutation. A no-op-equal set still bumps <see cref="Version"/>
    /// (consistent with the other setters); the closed-set guard is belt-and-braces behind the command validator.
    /// </summary>
    /// <param name="priority">A token in <c>{P0, P1, P2, P3}</c>, or null to clear.</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public void SetPriority(string? priority, DateTime utcNow)
    {
        Priority = NormalizePriority(priority);
        Touch(utcNow);
    }

    /// <summary>
    /// Reschedules the due date to a resolved UTC instant + <paramref name="dueHasTime"/> flag, or clears it
    /// with both null (slice 005, AS-05/R4). The <c>T</c> reschedule; realizes the reschedule slice 003
    /// deferred. The <c>{DueDate, DueHasTime}</c> pairing invariant (both set or both null) is enforced
    /// upstream by the reused validator (the slice-003 rule); the aggregate sets whatever the validated
    /// command supplies. Bumps <see cref="Version"/>.
    /// </summary>
    /// <param name="dueDate">The resolved due-date UTC instant, or null to clear.</param>
    /// <param name="dueHasTime">The <c>DueDate.has_time</c> flag, or null to clear.</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public void Reschedule(DateTime? dueDate, bool? dueHasTime, DateTime utcNow)
    {
        DueDate = dueDate;
        DueHasTime = dueHasTime;
        Touch(utcNow);
    }

    /// <summary>
    /// Whole-object replace of the editable fields (slice 005, AS-06/07/08, R4) — saved atomically on
    /// <c>Ctrl+Enter</c>. Reuses <see cref="NormalizeTitle"/> for the title, the closed-set guard for the
    /// priority, and sets the project the same way <see cref="MoveToProject"/> does (no duplicate move logic);
    /// the pairing invariant for the due fields is enforced upstream by the command validator. A single
    /// <see cref="Touch"/> (one mutation). The handler authorizes the task (and, on an actual project move,
    /// the target project) before calling this; the aggregate just records the replace.
    /// </summary>
    /// <param name="title">The new title; trimmed-non-empty and ≤ 500 chars.</param>
    /// <param name="description">The new description (markdown source), trimmed; whitespace-only → null; ≤ 8000 chars.</param>
    /// <param name="priority">A token in <c>{P0, P1, P2, P3}</c>, or null.</param>
    /// <param name="dueDate">The resolved due-date UTC instant, or null.</param>
    /// <param name="dueHasTime">The <c>DueDate.has_time</c> flag, or null.</param>
    /// <param name="projectId">The owning project, or null for the Inbox.</param>
    /// <param name="utcNow">The current UTC time (injected for testability).</param>
    public void EditTask(string title, string? description, string? priority, DateTime? dueDate, bool? dueHasTime, ProjectId? projectId, DateTime utcNow)
    {
        Title = NormalizeTitle(title);
        Description = NormalizeDescription(description);
        Priority = NormalizePriority(priority);
        DueDate = dueDate;
        DueHasTime = dueHasTime;
        ProjectId = projectId;
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

    /// <summary>Validates the closed priority set <c>{P0, P1, P2, P3}</c> or null (slice 005, R2).</summary>
    private static string? NormalizePriority(string? priority)
    {
        if (priority is null)
        {
            return null;
        }

        if (priority is not ("P0" or "P1" or "P2" or "P3"))
        {
            throw new ArgumentException("Priority must be one of: P0, P1, P2, P3 (or null).", nameof(priority));
        }

        return priority;
    }

    /// <summary>Trims the description (markdown source); whitespace-only → null; guards ≤ 8000 chars (slice 005, R3).</summary>
    private static string? NormalizeDescription(string? description)
    {
        if (description is null)
        {
            return null;
        }

        var trimmed = description.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Length > MaxDescriptionLength)
        {
            throw new ArgumentException($"Description must be {MaxDescriptionLength} characters or fewer.", nameof(description));
        }

        return trimmed;
    }

    private void Touch(DateTime utcNow)
    {
        UpdatedAt = utcNow;
        Version++;
    }
}
