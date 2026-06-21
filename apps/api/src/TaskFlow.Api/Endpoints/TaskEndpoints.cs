using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.TaskManagement;
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
}
