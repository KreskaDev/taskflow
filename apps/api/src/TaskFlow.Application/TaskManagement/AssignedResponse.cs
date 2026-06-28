namespace TaskFlow.Application.TaskManagement;

/// <summary>An "Assigned to me" group (slice 008, R6): a shared project and the caller's assigned tasks in it.</summary>
public sealed record AssignedGroup
{
    /// <summary>The shared project the assigned tasks belong to.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>The caller's assigned tasks in the project, ordered by the slice-005 R5 task order.</summary>
    public required IReadOnlyList<TaskResponse> Tasks { get; init; }
}

/// <summary>The "Assigned to me" envelope (slice 008, R6/FR-071): the caller's assigned tasks grouped by shared project.</summary>
public sealed record AssignedResponse
{
    /// <summary>The project groups (ordered by project id).</summary>
    public required IReadOnlyList<AssignedGroup> Groups { get; init; }
}
