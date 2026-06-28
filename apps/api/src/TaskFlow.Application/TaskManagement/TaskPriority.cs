namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The closed priority token set <c>{P0, P1, P2, P3}</c> (slice 005, R2) and its trust-boundary check,
/// shared by the <c>SetPriority</c> and <c>EditTask</c> validators so both boundaries stay in lockstep with
/// the domain <c>Task.NormalizePriority</c> guard. NULL = unprioritized (a valid value — sorts last, R5).
/// </summary>
public static class TaskPriority
{
    /// <summary>True iff <paramref name="priority"/> is null or one of the closed-set tokens (R2).</summary>
    public static bool IsValid(string? priority) =>
        priority is null or "P0" or "P1" or "P2" or "P3";
}
