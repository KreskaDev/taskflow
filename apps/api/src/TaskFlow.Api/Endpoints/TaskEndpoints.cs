using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.TaskManagement;
using TaskFlow.Application.TaskManagement.Commands;
using TaskFlow.Application.TaskManagement.Queries;
using Wolverine;
using Wolverine.Http;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Api.Endpoints;

/// <summary>
/// HTTP surface for the Task aggregate (contracts/openapi.yaml). Each endpoint is a thin transport
/// adapter that dispatches the corresponding command/query through Wolverine's local message pipeline
/// via <see cref="IMessageBus.InvokeAsync{T}"/>, so the deny-by-default authorization middleware (T019)
/// and the FluentValidation boundary are woven ahead of every handler.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine.Http discovers and maps HTTP endpoints only on public types; this class must stay public.")]
public static class TaskEndpoints
{
    /// <summary>
    /// Idempotent insert-if-not-exists of the caller's task, keyed on the client-generated id carried in
    /// the route (FR-001). The owner is resolved from <c>ICurrentUser</c> inside the handler — never the
    /// body — so a caller can only ever create a task it owns. Returns 200 on both first insert and replay.
    /// </summary>
    [WolverinePut("/api/tasks/{id}")]
    public static Task<TaskResponse> Create(Guid id, CreateTaskRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<TaskResponse>(new CreateTask
        {
            Id = TaskId.From(id),
            Title = request.Title,
            Position = request.Position,
        });
    }

    /// <summary>Return the current caller's own non-deleted tasks, ordered by position then id (FR-007).</summary>
    [WolverineGet("/api/tasks")]
    public static Task<IReadOnlyList<TaskResponse>> List(IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<IReadOnlyList<TaskResponse>>(new GetMyTasks());
    }

    /// <summary>
    /// Rename the caller's own task under the optimistic-concurrency <c>version</c> guard (FR-001, R4).
    /// The owner is resolved from <c>ICurrentUser</c> in the handler — never the body. A foreign/absent/
    /// soft-deleted id → 404; a stale <c>version</c> → 409; an empty/>500 title → 422.
    /// </summary>
    [WolverinePatch("/api/tasks/{id}/title")]
    public static Task<TaskResponse> Rename(Guid id, RenameTaskRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<TaskResponse>(new RenameTask
        {
            Id = TaskId.From(id),
            Title = request.Title,
            Version = request.Version,
        });
    }

    /// <summary>
    /// Set the caller's own task to a DESIRED status (<c>done</c>|<c>backlog</c>) under the
    /// optimistic-concurrency <c>version</c> guard (FR-003, R3/R4) — idempotent, not a blind flip. The
    /// owner is resolved from <c>ICurrentUser</c> in the handler. Foreign/absent/soft-deleted id → 404;
    /// a stale <c>version</c> → 409; an out-of-range target → 422.
    /// </summary>
    [WolverinePatch("/api/tasks/{id}/status")]
    public static Task<TaskResponse> SetStatus(Guid id, SetTaskStatusRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<TaskResponse>(new SetTaskDone
        {
            Id = TaskId.From(id),
            Status = request.Status,
            Version = request.Version,
        });
    }

    /// <summary>
    /// Move the caller's own task to a new client-computed fractional rank under the
    /// optimistic-concurrency <c>version</c> guard (FR-102, R4/R5). The server validates the rank format
    /// and is the sole writer; it never generates ranks. Foreign/absent/soft-deleted id → 404; a stale
    /// <c>version</c> → 409; a malformed rank → 422.
    /// </summary>
    [WolverinePatch("/api/tasks/{id}/position")]
    public static Task<TaskResponse> Reorder(Guid id, ReorderTaskRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<TaskResponse>(new ReorderTask
        {
            Id = TaskId.From(id),
            Position = request.Position,
            Version = request.Version,
        });
    }

    /// <summary>
    /// Soft-delete the caller's own task (FR-097, R8) — version-free and idempotent (a re-delete of the
    /// caller's own tombstone is the 204 no-op). The owner is resolved from <c>ICurrentUser</c> in the
    /// handler; a foreign/absent id → 404. Returns 204 No Content.
    /// </summary>
    [WolverineDelete("/api/tasks/{id}")]
    public static Task Delete(Guid id, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync(new DeleteTask { Id = TaskId.From(id) });
    }
}
