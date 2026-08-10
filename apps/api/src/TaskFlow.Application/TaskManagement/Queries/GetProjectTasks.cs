using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using DomainProject = TaskFlow.Domain.TaskManagement.Project;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;

namespace TaskFlow.Application.TaskManagement.Queries;

/// <summary>
/// Lists a project's tasks (contracts/openapi.yaml <c>listProjectTasks</c>) — PROJECT-scoped
/// (slice 010 D4/FR-066): <c>WHERE project_id = {id} AND deleted_at IS NULL ORDER BY position, id</c>,
/// regardless of which member authored each task. The caller is resolved from
/// <see cref="ICurrentUser"/>, never a wire field (R13). A foreign/absent project → 404
/// (existence not disclosed), enforced in the handler.
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
        IProjectMembershipRepository members,
        ITaskRepository tasks,
        Labels.ITaskLabelRepository taskLabels,
        IResourceAuthorizationPolicy authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(taskLabels);
        ArgumentNullException.ThrowIfNull(authorization);

        // 404-FIRST: a foreign/absent project, or a shared project the caller is not a member of, must not
        // leak as a 200 empty list (R13/R9). For a personal project FindReadableAsync is equivalent to the
        // slice-004 owner-scoped find (the OR short-circuits to owner_id), so the personal arm is unchanged.
        var project = await projects
            .FindReadableAsync(query.ProjectId, currentUser.Id, cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
        {
            throw new NotFoundException();
        }

        // Shared arm: any current member (viewer+) may READ (the policy contract — R8/R9). The personal arm
        // needs no membership lookup (the readable load already proved ownership).
        if (project.Visibility == DomainProject.SharedVisibility)
        {
            var memberships = await members.ListByProjectAsync(query.ProjectId, cancellationToken).ConfigureAwait(false);
            authorization.RequireRole(project, memberships, EffectiveRole.Viewer);
        }

        // A shared project's tasks belong to the PROJECT (FR-066) — the listing is project-scoped
        // (slice 010 D4): member-authored tasks are visible to every current member; createdBy is
        // provenance, not a visibility filter. For a personal project the author can only be the
        // owner, so the personal arm is unchanged.
        var projectTasks = await tasks
            .ListByProjectAsync(query.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        // Caller-scoped labels (slice 006, R6): the CALLER's own labels on these tasks (owner = the caller,
        // not the project owner) — one batched join.
        var labelsByTask = await taskLabels
            .ListLabelIdsForTasksAsync(projectTasks.Select(t => t.Id).ToList(), currentUser.Id, cancellationToken)
            .ConfigureAwait(false);

        return projectTasks
            .Select(t => TaskResponse.From(t, labelsByTask.TryGetValue(t.Id, out var ids) ? ids : []))
            .ToList();
    }
}
