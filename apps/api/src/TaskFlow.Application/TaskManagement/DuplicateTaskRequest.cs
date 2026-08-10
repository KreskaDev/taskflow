namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// HTTP body of <c>POST /api/tasks/{id}/duplicate</c> (slice 019, contracts/task-duplicate.md).
/// <see cref="NewTaskId"/> is <c>required</c> at the serializer level, so a missing key (like a
/// malformed uuid) fails JSON binding as a 400 — the contract's "400 validation (missing/malformed
/// newTaskId)" — before any handler runs.
/// </summary>
public sealed record DuplicateTaskRequest
{
    /// <summary>The client-generated uuid the duplicate will carry (idempotency + optimistic paint).</summary>
    public required Guid NewTaskId { get; init; }
}
