using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;

namespace TaskFlow.Application.TaskManagement.Queries;

/// <summary>
/// Lists a project's tasks (contracts/openapi.yaml <c>listProjectTasks</c>, research R6) — owner + project
/// scoped: <c>WHERE created_by = caller AND deleted_at IS NULL AND project_id = {id} ORDER BY position,
/// id</c>. The owner is resolved from <see cref="ICurrentUser"/>, never a wire field (R13). A
/// foreign/absent project → 404 (existence not disclosed), enforced in the handler.
/// </summary>
public sealed record GetProjectTasks
{
    /// <summary>The project identity, carried in the route.</summary>
    public required ProjectId ProjectId { get; init; }
}

/// <summary>
/// Handles <see cref="GetProjectTasks"/>. Authentication is enforced upstream by the deny-by-default
/// middleware; this handler owns the ownership-404 + owner-scoped read.
/// </summary>
/// <remarks>
/// Decision path: resolve the project as OWNED FIRST (<see cref="IProjectRepository.FindOwnedAsync"/>) so a
/// foreign/absent/tombstoned id → 404 (R13) — NOT a leaky 200 empty list (the task query alone would return
/// <c>[]</c> for a foreign project, disclosing nothing but also never signalling not-found). Only once the
/// project is confirmed caller-owned does it list the project's NON-deleted tasks
/// (<see cref="ITaskRepository.ListByProjectAsync"/>), projecting each to its lean
/// <see cref="TaskResponse"/> wire model.
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-002 GetMyTasksHandler).")]
public static class GetProjectTasksHandler
{
    public static async Task<IReadOnlyList<TaskResponse>> Handle(
        GetProjectTasks query,
        ICurrentUser currentUser,
        IProjectRepository projects,
        ITaskRepository tasks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(tasks);

        var owner = currentUser.Id;

        // 404-FIRST ownership: a foreign/absent project must not leak as a 200 empty list (R13).
        var project = await projects
            .FindOwnedAsync(query.ProjectId, owner, cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
        {
            throw new NotFoundException();
        }

        var owned = await tasks
            .ListByProjectAsync(query.ProjectId, owner, cancellationToken)
            .ConfigureAwait(false);

        return owned.Select(TaskResponse.From).ToList();
    }
}
