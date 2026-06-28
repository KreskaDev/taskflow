using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.TaskManagement;
using TaskFlow.Application.TaskManagement.Queries;
using Wolverine;
using Wolverine.Http;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;

namespace TaskFlow.Api.Endpoints;

/// <summary>
/// HTTP surface for the Project aggregate (contracts/openapi.yaml, slice 004). Each endpoint is a thin
/// transport adapter that dispatches the corresponding command/query through Wolverine's local message
/// pipeline via <see cref="IMessageBus.InvokeAsync{T}"/>, so the deny-by-default authorization
/// middleware and the FluentValidation boundary are woven ahead of every handler. Mirrors
/// <see cref="TaskEndpoints"/>.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine.Http discovers and maps HTTP endpoints only on public types; this class must stay public.")]
public static class ProjectEndpoints
{
    /// <summary>
    /// Idempotent insert-if-not-exists of the caller's project, keyed on the client-generated id carried
    /// in the route (FR-001). The owner is resolved from <c>ICurrentUser</c> inside the handler — never
    /// the body. A foreign parent → 404 (before nesting); an owned-but-illegal parent → 422. Returns 200
    /// on both first insert and idempotent replay.
    /// </summary>
    [WolverinePut("/api/projects/{id}")]
    public static Task<ProjectResponse> Create(Guid id, CreateProjectRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<ProjectResponse>(new CreateProject
        {
            Id = ProjectId.From(id),
            Name = request.Name,
            Color = request.Color,
            Icon = request.Icon,
            ParentId = request.ParentId is { } parentId ? ProjectId.From(parentId) : null,
        });
    }

    /// <summary>
    /// List the caller's own projects (owner-scoped, tombstone-excluded, R8/R13). Default
    /// (<c>archived=false</c>) returns the ACTIVE set for the sidebar (AS-05); <c>archived=true</c> returns
    /// the archived set so unarchive (AS-11) is reachable. Flat list; the one-level tree is assembled
    /// client-side (R16).
    /// </summary>
    [WolverineGet("/api/projects")]
    public static Task<IReadOnlyList<ProjectResponse>> List(IMessageBus bus, bool archived = false)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<IReadOnlyList<ProjectResponse>>(new GetMyProjects { Archived = archived });
    }

    /// <summary>
    /// Whole-object edit of the caller's own project (name/color/icon/parent together, research R4) under
    /// the optimistic-concurrency <c>version</c> guard. The owner is resolved from <c>ICurrentUser</c> in
    /// the handler. A foreign id (or foreign re-parent target) → 404; a stale <c>version</c> → 409; an
    /// out-of-preset value or an illegal nesting → 422.
    /// </summary>
    [WolverinePatch("/api/projects/{id}")]
    public static Task<ProjectResponse> Edit(Guid id, EditProjectRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<ProjectResponse>(new EditProject
        {
            Id = ProjectId.From(id),
            Name = request.Name,
            Color = request.Color,
            Icon = request.Icon,
            ParentId = request.ParentId is { } parentId ? ProjectId.From(parentId) : null,
            Version = request.Version,
        });
    }

    /// <summary>
    /// Archive the caller's own project (reversible state, FR-013/AS-05) under the optimistic
    /// <c>version</c> guard. When the project has children, <c>childDisposition</c> is REQUIRED (AS-10).
    /// A foreign id → 404; a stale <c>version</c> → 409; a missing/invalid child disposition → 422.
    /// </summary>
    [WolverinePatch("/api/projects/{id}/archive")]
    public static Task<ProjectResponse> Archive(Guid id, ArchiveProjectRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<ProjectResponse>(new ArchiveProject
        {
            Id = ProjectId.From(id),
            Version = request.Version,
            ChildDisposition = request.ChildDisposition,
        });
    }

    /// <summary>
    /// Unarchive the caller's own project (AS-11) under the optimistic <c>version</c> guard. A child whose
    /// parent is still archived/deleted is restored TOP-LEVEL (R9). A foreign id → 404; a stale
    /// <c>version</c> → 409.
    /// </summary>
    [WolverinePatch("/api/projects/{id}/unarchive")]
    public static Task<ProjectResponse> Unarchive(Guid id, VersionOnlyRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<ProjectResponse>(new UnarchiveProject
        {
            Id = ProjectId.From(id),
            Version = request.Version,
        });
    }

    /// <summary>
    /// Share the caller's own personal project (personal → shared, FR-058). Owner-only: a non-owner → 404
    /// (the project is still personal, so existence is not disclosed). A stale <c>version</c> → 409.
    /// Confirmation-gated, NOT under the 30s undo (FR-064).
    /// </summary>
    [WolverinePatch("/api/projects/{id}/share")]
    public static Task<ProjectResponse> Share(Guid id, VersionOnlyRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<ProjectResponse>(new ShareProject
        {
            Id = ProjectId.From(id),
            Version = request.Version,
        });
    }

    /// <summary>
    /// Unshare a shared project (shared → personal, FR-058/FR-059), removing ALL membership rows in the
    /// same transaction. Owner-only manage op: an insufficient-role member → 403, a non-member → 404. A
    /// stale <c>version</c> → 409. Confirmation-gated with a blast-radius preview (FR-064).
    /// </summary>
    [WolverinePatch("/api/projects/{id}/unshare")]
    public static Task<ProjectResponse> Unshare(Guid id, VersionOnlyRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<ProjectResponse>(new UnshareProject
        {
            Id = ProjectId.From(id),
            Version = request.Version,
        });
    }

    /// <summary>
    /// Transfer ownership of a shared project to a current member (FR-094) — the only move of the immutable
    /// <c>ownerId</c>. Owner-only: editor/viewer member → 403, non-member → 404. Target who is a non-member or
    /// the current owner → 422; a stale <c>version</c> → 409. Returns the project with the caller's new
    /// (editor) role.
    /// </summary>
    [WolverinePatch("/api/projects/{id}/owner")]
    public static Task<ProjectResponse> TransferOwner(Guid id, TransferOwnershipRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<ProjectResponse>(new TransferOwnership
        {
            Id = ProjectId.From(id),
            NewOwnerId = TaskFlow.Domain.IdentityAccess.UserId.From(request.UserId),
            Version = request.Version,
        });
    }

    /// <summary>
    /// List the caller's own tasks under a project (owner + project scoped, R6). A foreign/absent project →
    /// 404 (the ownership posture, R13) — never a leaky 200 empty list. Reuses <c>TaskResponse</c>.
    /// </summary>
    [WolverineGet("/api/projects/{id}/tasks")]
    public static Task<IReadOnlyList<TaskResponse>> ListTasks(Guid id, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<IReadOnlyList<TaskResponse>>(new GetProjectTasks { ProjectId = ProjectId.From(id) });
    }

    /// <summary>
    /// Soft-delete the caller's own project (FR-014/EC-03/AS-10), applying task + child dispositions BEFORE
    /// the tombstone (research R5). <c>version</c>/<c>taskDisposition</c>/<c>childDisposition</c> ride QUERY
    /// params (HTTP DELETE bodies are poorly supported). VERSIONED, NOT idempotent: a stale <c>version</c> →
    /// 409. <c>archive_with_tasks</c> archives instead of deleting. A foreign id → 404; a missing/invalid
    /// required disposition → 422. Returns 204.
    /// </summary>
    [WolverineDelete("/api/projects/{id}")]
    public static Task Delete(Guid id, int version, IMessageBus bus, string? taskDisposition = null, string? childDisposition = null)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync(new DeleteProject
        {
            Id = ProjectId.From(id),
            Version = version,
            TaskDisposition = taskDisposition,
            ChildDisposition = childDisposition,
        });
    }
}
