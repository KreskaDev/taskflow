using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.TaskManagement;
using TaskFlow.Application.TaskManagement.Commands;
using TaskFlow.Application.TaskManagement.Queries;
using Wolverine;
using Wolverine.Http;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;
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
            DueDate = request.DueDate,
            DueHasTime = request.DueHasTime,
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
    /// The Today view (slice 005, AS-01/AS-02): the caller's readable tasks due today-in-Warsaw or
    /// overdue-incomplete, grouped by project, R5-ordered (research R5/R6). The literal <c>/today</c> segment
    /// is registered ahead of any <c>/{id}</c> template so it wins (data-model §6 routing note).
    /// </summary>
    [WolverineGet("/api/tasks/today")]
    public static Task<TodayResponse> Today(IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<TodayResponse>(new GetTodayTasks());
    }

    /// <summary>
    /// The Upcoming view (slice 005, US-08.AS-02): the caller's readable tasks in the next 7 Warsaw days,
    /// grouped by Warsaw day, R5-ordered (research R5/R6). The literal <c>/upcoming</c> segment wins over
    /// <c>/{id}</c>.
    /// </summary>
    [WolverineGet("/api/tasks/upcoming")]
    public static Task<UpcomingResponse> Upcoming(IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<UpcomingResponse>(new GetUpcomingTasks());
    }

    /// <summary>
    /// The "Assigned to me" view (slice 008, AS-03): the caller's tasks across shared projects where they are
    /// a current member (or owner) AND an assignee, grouped by project (research R6/FR-071). The literal
    /// <c>/assigned</c> segment wins over <c>/{id}</c>.
    /// </summary>
    [WolverineGet("/api/tasks/assigned")]
    public static Task<AssignedResponse> Assigned(IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<AssignedResponse>(new GetAssignedToMe());
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
    /// Move the caller's own task to a project, or to the Inbox (the <c>M</c> action, US-08.AS-05, R7) under
    /// the optimistic-concurrency <c>version</c> guard. <c>projectId = null</c> moves the task to the Inbox.
    /// The owner is resolved from <c>ICurrentUser</c> in the handler, which checks ownership of BOTH the task
    /// AND the target project — either failing → 404 (never 403); a stale <c>version</c> → 409.
    /// </summary>
    [WolverinePatch("/api/tasks/{id}/project")]
    public static Task<TaskResponse> Move(Guid id, MoveTaskToProjectRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<TaskResponse>(new MoveTaskToProject
        {
            Id = TaskId.From(id),
            ProjectId = request.ProjectId is { } projectId ? ProjectId.From(projectId) : null,
            Version = request.Version,
        });
    }

    /// <summary>
    /// Set the task's priority (the <c>1</c>-<c>4</c> keys, AS-04), or clear it, under the optimistic
    /// <c>version</c> guard (slice 005, R2/R4). Authorization is dispatched by the containing project's
    /// visibility in the handler: personal → ownership (foreign → 404); shared → editor/owner (viewer → 403,
    /// non-member → 404). Out-of-set priority → 422; stale <c>version</c> → 409.
    /// </summary>
    [WolverinePatch("/api/tasks/{id}/priority")]
    public static Task<TaskResponse> SetPriority(Guid id, SetPriorityRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<TaskResponse>(new SetPriority
        {
            Id = TaskId.From(id),
            Priority = request.Priority,
            Version = request.Version,
        });
    }

    /// <summary>
    /// Reschedule the task's due date to a client-resolved UTC instant (the <c>T</c> key, AS-05), or clear it,
    /// under the optimistic <c>version</c> guard (slice 005, R4). Same dispatch-by-visibility authorization as
    /// set-priority. A bad due pair / non-UTC kind / implausible range → 422; stale <c>version</c> → 409.
    /// </summary>
    [WolverinePatch("/api/tasks/{id}/due-date")]
    public static Task<TaskResponse> RescheduleDueDate(Guid id, RescheduleDueDateRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<TaskResponse>(new RescheduleDueDate
        {
            Id = TaskId.From(id),
            DueDate = request.DueDate,
            DueHasTime = request.DueHasTime,
            Version = request.Version,
        });
    }

    /// <summary>
    /// The combined task editor (the <c>E</c> editor, AS-06/07/08): saves title, description, priority, due
    /// date, and project together — a whole-object replace, atomic on <c>Ctrl+Enter</c> — under the
    /// optimistic <c>version</c> guard (slice 005, R4). Same dispatch-by-visibility authorization; an omitted
    /// field → 422; an actual project move to a foreign/absent project → 404; stale <c>version</c> → 409.
    /// </summary>
    [WolverinePatch("/api/tasks/{id}/edit")]
    public static Task<TaskResponse> Edit(Guid id, EditTaskRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<TaskResponse>(new EditTask
        {
            Id = TaskId.From(id),
            Title = request.Title,
            Description = request.Description,
            Priority = request.Priority,
            DueDate = request.DueDate,
            DueHasTime = request.DueHasTime,
            ProjectId = request.ProjectId is { } projectId ? ProjectId.From(projectId) : null,
            Version = request.Version,
        });
    }

    /// <summary>
    /// Set a shared-project task's assignee set — a whole-set replace (slice 008, AS-01/AS-02). Authorized in
    /// the handler: editor/owner of the SHARED project (viewer → 403; non-member / personal task → 404); every
    /// assignee MUST be a current member (else 422); stale <c>version</c> → 409. Raises <c>TaskAssigned</c>.
    /// </summary>
    [WolverinePatch("/api/tasks/{id}/assignees")]
    public static Task<TaskResponse> SetAssignees(Guid id, SetTaskAssigneesRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<TaskResponse>(new SetTaskAssignees
        {
            Id = TaskId.From(id),
            AssigneeIds = request.AssigneeIds,
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
