namespace TaskFlow.Application.TaskManagement;

/// <summary>An Upcoming group: a Warsaw calendar day and its R5-ordered rows (slice 005, R5/R6).</summary>
public sealed record UpcomingGroup
{
    /// <summary>
    /// The Warsaw calendar date (<c>YYYY-MM-DD</c>) — DISTINCT from each task's UTC <c>dueDate</c>; computed
    /// via <c>WarsawDayBounds.WarsawLocalDate</c>, never the truncated UTC date (R1/R3).
    /// </summary>
    public required string Date { get; init; }

    /// <summary>The day's rows, ordered priority (P0 first, null last) → due time → createdAt → id (R5).</summary>
    public required IReadOnlyList<TaskResponse> Tasks { get; init; }
}

/// <summary>The Upcoming view envelope (slice 005, R6): tasks grouped by Warsaw day, groups ascending by date.</summary>
public sealed record UpcomingResponse
{
    /// <summary>The day groups, ordered ascending by <see cref="UpcomingGroup.Date"/>.</summary>
    public required IReadOnlyList<UpcomingGroup> Groups { get; init; }
}
