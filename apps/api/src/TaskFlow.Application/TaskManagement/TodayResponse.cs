using TaskEntity = TaskFlow.Domain.TaskManagement.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// A Today row (slice 005, R5/R6): the lean <see cref="TaskResponse"/> fields PLUS an
/// <see cref="IsOverdue"/> flag. Flattened (the contract's <c>allOf TaskResponse + isOverdue</c>) so the
/// generated client sees <c>isOverdue</c> as a sibling of the task fields. <c>isOverdue</c> lives ONLY on the
/// Today read model — never on base <see cref="TaskResponse"/> (data-model §10).
/// </summary>
public sealed record TodayTaskResponse
{
    /// <summary>The client-generated UUIDv7 identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>The task title (≤ 500 chars).</summary>
    public required string Title { get; init; }

    /// <summary>Lifecycle status (lowercase wire token).</summary>
    public required string Status { get; init; }

    /// <summary>The persisted lexicographic rank string.</summary>
    public required string Position { get; init; }

    /// <summary>Optimistic-concurrency token.</summary>
    public required int Version { get; init; }

    /// <summary>UTC creation timestamp.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>UTC last-mutation timestamp.</summary>
    public required DateTime UpdatedAt { get; init; }

    /// <summary>UTC completion timestamp; set iff <c>status = done</c>, else null.</summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>The resolved due-date UTC instant, or null.</summary>
    public DateTime? DueDate { get; init; }

    /// <summary>The <c>has_time</c> flag; null when <see cref="DueDate"/> is null.</summary>
    public bool? DueHasTime { get; init; }

    /// <summary>The owning project's id, or null when the task is in the Inbox.</summary>
    public Guid? ProjectId { get; init; }

    /// <summary>The priority token <c>P0</c>–<c>P3</c>, or null = unprioritized.</summary>
    public string? Priority { get; init; }

    /// <summary>The description (markdown source), or null. Output-escaped on render (FR-099).</summary>
    public string? Description { get; init; }

    /// <summary>The assignee user ids (slice 008); empty for personal/unassigned. Ids only.</summary>
    public required IReadOnlyList<Guid> Assignees { get; init; }

    /// <summary>
    /// The CALLER's OWN label ids applied to this task (slice 006, R6). ALWAYS present; EMPTY when none.
    /// Caller-scoped. This flattened DTO carries its own <c>labels</c> — the required <c>TaskResponse.From</c>
    /// parameter does NOT auto-propagate here (the fields are hand-copied), so <see cref="From"/> threads it.
    /// </summary>
    public required IReadOnlyList<Guid> Labels { get; init; }

    /// <summary>True when <c>due_date</c> is before the start of today-Warsaw (overdue incomplete). Today-only.</summary>
    public required bool IsOverdue { get; init; }

    /// <summary>Projects a <see cref="TaskEntity"/> + the derived overdue flag + the caller's label ids to the Today wire row.</summary>
    public static TodayTaskResponse From(TaskEntity task, bool isOverdue, IReadOnlyList<Guid> callerLabelIds)
    {
        ArgumentNullException.ThrowIfNull(task);
        var t = TaskResponse.From(task, callerLabelIds);
        return new TodayTaskResponse
        {
            Id = t.Id,
            Title = t.Title,
            Status = t.Status,
            Position = t.Position,
            Version = t.Version,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
            CompletedAt = t.CompletedAt,
            DueDate = t.DueDate,
            DueHasTime = t.DueHasTime,
            ProjectId = t.ProjectId,
            Priority = t.Priority,
            Description = t.Description,
            Assignees = t.Assignees,
            Labels = t.Labels,
            IsOverdue = isOverdue,
        };
    }
}

/// <summary>A Today group: the owning project (null = Inbox/unprojected) and its R5-ordered rows.</summary>
public sealed record TodayGroup
{
    /// <summary>The group's owning project; null = the Inbox/unprojected group.</summary>
    public required Guid? ProjectId { get; init; }

    /// <summary>The group's rows, ordered priority (P0 first, null last) → due time → createdAt → id (R5).</summary>
    public required IReadOnlyList<TodayTaskResponse> Tasks { get; init; }
}

/// <summary>The Today view envelope (slice 005, R6): tasks grouped by project.</summary>
public sealed record TodayResponse
{
    /// <summary>The project groups (Inbox first, then by project id).</summary>
    public required IReadOnlyList<TodayGroup> Groups { get; init; }
}
