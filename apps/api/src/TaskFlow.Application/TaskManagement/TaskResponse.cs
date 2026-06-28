using TaskFlow.Domain.TaskManagement;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskStatus = TaskFlow.Domain.TaskManagement.TaskStatus;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The lean Task read model (contracts/openapi.yaml, research R15), shared by create and list.
/// Carries EXACTLY the eight fields the BFF/UI needs — including <see cref="Version"/> so the
/// optimistic-concurrency token round-trips (without it the 409 path can never be exercised).
/// NEVER exposes <c>deleted_at</c> (soft-deleted rows are never returned) or any reserved
/// forward-compat column; <c>createdBy</c> is omitted (always the caller).
/// </summary>
public sealed record TaskResponse
{
    /// <summary>The client-generated UUIDv7 identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>The task title (≤ 500 chars).</summary>
    public required string Title { get; init; }

    /// <summary>Lifecycle status — the full FR-003 enum, lowercase (only <c>backlog</c>/<c>done</c> reachable this slice).</summary>
    public required string Status { get; init; }

    /// <summary>The persisted lexicographic rank string; the canonical ascending sort key (R5).</summary>
    public required string Position { get; init; }

    /// <summary>Optimistic-concurrency token; incremented on every mutating write (R4).</summary>
    public required int Version { get; init; }

    /// <summary>UTC creation timestamp (FR-004).</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>UTC last-mutation timestamp (FR-004).</summary>
    public required DateTime UpdatedAt { get; init; }

    /// <summary>UTC completion timestamp; set iff <c>status = done</c>, else null (FR-003/FR-004).</summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>The resolved due-date UTC instant (FR-092, R8), or null for no due date.</summary>
    public DateTime? DueDate { get; init; }

    /// <summary>The <c>DueDate.has_time</c> flag (R2/R8); null when <see cref="DueDate"/> is null.</summary>
    public bool? DueHasTime { get; init; }

    /// <summary>The owning project's id (slice 004, R16), or null when the task is in the Inbox (FR-021).</summary>
    public Guid? ProjectId { get; init; }

    /// <summary>The priority token <c>P0</c>–<c>P3</c> (slice 005, R2), or null = unprioritized. Nullable; NOT in <c>required[]</c>.</summary>
    public string? Priority { get; init; }

    /// <summary>The description (markdown source, slice 005, R3), or null. Output-escaped on render (FR-099). Nullable; NOT in <c>required[]</c>.</summary>
    public string? Description { get; init; }

    /// <summary>The assignee user ids (slice 008, R7). ALWAYS present; EMPTY for personal/unassigned tasks. Ids only (names via the roster).</summary>
    public required IReadOnlyList<Guid> Assignees { get; init; }

    /// <summary>Projects a <see cref="TaskEntity"/> aggregate to its lean wire model (mirrors <c>UserProfile.From</c>).</summary>
    public static TaskResponse From(TaskEntity task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new TaskResponse
        {
            Id = task.Id.Value,
            Title = task.Title,
            Status = ToWireStatus(task.Status),
            Position = task.Position,
            Version = task.Version,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
            CompletedAt = task.CompletedAt,
            DueDate = task.DueDate,
            DueHasTime = task.DueHasTime,
            ProjectId = task.ProjectId?.Value,
            Priority = task.Priority,
            Description = task.Description,
            Assignees = task.Assignees.Select(a => a.UserId.Value).ToList(),
        };
    }

    /// <summary>
    /// Maps the domain status to its lowercase snake_case wire token (the full FR-003 enum,
    /// matching the OpenAPI <c>TaskResponse.status</c> enum and the DB converter in
    /// <c>TaskConfiguration</c> — the PascalCase member names are never encoded into the wire string).
    /// </summary>
    private static string ToWireStatus(TaskStatus status) => status switch
    {
        TaskStatus.Backlog => "backlog",
        TaskStatus.Todo => "todo",
        TaskStatus.InProgress => "in_progress",
        TaskStatus.Done => "done",
        TaskStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown task status."),
    };
}
