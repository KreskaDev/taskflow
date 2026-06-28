namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The deterministic within-group triage order shared by the Today and Upcoming views (slice 005, R5):
/// priority (P0 first) → due time → createdAt → id. <b>NULL priority sorts LAST</b> (after P3 — unprioritized
/// is the lowest triage rank). A date-only task's <c>due_date</c> is stored at the start of its Warsaw day
/// (slice 003), so ordering by the <c>due_date</c> instant naturally sorts a date-only task as start-of-day,
/// before same-day timed tasks (R5).
/// </summary>
internal static class TaskTriageOrder
{
    /// <summary>Maps a priority token to its sort rank; null (unprioritized) ranks LAST (R2/R5).</summary>
    public static int PriorityRank(string? priority) => priority switch
    {
        "P0" => 0,
        "P1" => 1,
        "P2" => 2,
        "P3" => 3,
        _ => 4,
    };
}
