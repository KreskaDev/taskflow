using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using DomainProject = TaskFlow.Domain.TaskManagement.Project;
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
        IProjectMembershipRepository members,
        ITaskRepository tasks,
        IResourceAuthorizationPolicy authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(tasks);
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

        // A shared project's tasks belong to the PROJECT — scope by the owner (this slice, members cannot yet
        // author tasks; slice 008 introduces multi-author task listing). For a personal project the owner is
        // the caller, so this matches the slice-004 owner-scoped list exactly.
        var projectTasks = await tasks
            .ListByProjectAsync(query.ProjectId, project.OwnerId, cancellationToken)
            .ConfigureAwait(false);

        return projectTasks.Select(TaskResponse.From).ToList();
    }
}
