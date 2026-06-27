using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.TaskManagement;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Soft-deletes the caller's own project (FR-014/EC-03/AS-10, contracts/openapi.yaml <c>deleteProject</c>)
/// applying caller-chosen task + child dispositions in-transaction BEFORE the tombstone (research R5).
/// VERSIONED, NOT idempotent (unlike the task delete): it applies real disposition mutations, so a stale
/// <see cref="Version"/> → 409. <c>archive_with_tasks</c> ARCHIVES the project (no tombstone) keeping its
/// tasks. The caller is resolved from <see cref="ICurrentUser"/>.
/// </summary>
/// <remarks>
/// This is the HTTP request bound by <c>DELETE /api/projects/{id}</c>: <see cref="Id"/> binds from the
/// route, the rest from QUERY params (HTTP DELETE bodies are poorly supported by tooling/proxies).
/// </remarks>
public sealed record DeleteProject
{
    /// <summary>The project identity, carried in the route.</summary>
    public required ProjectId Id { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token; a stale value → 409.</summary>
    public required int Version { get; init; }

    /// <summary>
    /// The task disposition (FR-014/EC-03): <c>cascade</c> | <c>move_to_inbox</c> | <c>archive_with_tasks</c>.
    /// REQUIRED when the project has tasks (enforced in the handler); null/omitted for a taskless project.
    /// </summary>
    public string? TaskDisposition { get; init; }

    /// <summary>
    /// The child disposition (AS-10): <c>cascade</c> | <c>orphan_to_top</c>. REQUIRED when the project has
    /// child projects (enforced in the handler); null/omitted for a childless project.
    /// </summary>
    public string? ChildDisposition { get; init; }
}

/// <summary>
/// Handles <see cref="DeleteProject"/> under the optimistic <c>version</c> rule, applying the dispositions
/// (R5) before the tombstone/archive. Authentication is enforced upstream by the deny-by-default middleware.
/// </summary>
/// <remarks>
/// Decision path (all one per-message transaction):
/// <list type="bullet">
/// <item>owner-scoped + NON-deleted load (foreign/absent/tombstoned → 404, R13).</item>
/// <item>stale version → 409 BEFORE any mutation (the delete is versioned, NOT idempotent).</item>
/// <item>task disposition (when the project has tasks): a missing/invalid token → 422; <c>cascade</c>
/// soft-deletes each task (domain method); <c>move_to_inbox</c> nulls their <c>project_id</c> (repository
/// set-update); <c>archive_with_tasks</c> leaves the tasks and flips the parent's fate to ARCHIVE.</item>
/// <item>child disposition (when the parent has children): shared with archive (AS-10) — cascade follows
/// the parent's resolved fate (archive vs soft-delete), orphan_to_top promotes the children.</item>
/// <item>finally archive (archive_with_tasks) or soft-delete (tombstone) the parent.</item>
/// </list>
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-002 handlers).")]
public static class DeleteProjectHandler
{
    public static async Task Handle(
        DeleteProject command,
        ICurrentUser currentUser,
        IProjectRepository projects,
        ITaskRepository tasks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(tasks);

        var owner = currentUser.Id;

        var project = await projects
            .FindOwnedAsync(command.Id, owner, cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
        {
            throw new NotFoundException();
        }

        if (project.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        var utcNow = DateTime.UtcNow;

        // archive_with_tasks resolves the parent's fate to ARCHIVE (no tombstone), which the child cascade
        // must follow (R5: the whole subtree shares the reversible disposition).
        var archiveInsteadOfDelete = command.TaskDisposition == ProjectDispositions.ArchiveWithTasks;

        await ApplyTaskDispositionAsync(command, owner, projects, tasks, utcNow, cancellationToken).ConfigureAwait(false);

        await ProjectDispositions
            .ApplyChildDispositionAsync(command.Id, owner, command.ChildDisposition, cascadeArchive: archiveInsteadOfDelete, utcNow, projects, cancellationToken)
            .ConfigureAwait(false);

        if (archiveInsteadOfDelete)
        {
            project.Archive(utcNow);
        }
        else
        {
            project.SoftDelete(utcNow);
        }

        await projects.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies the task disposition (FR-014/EC-03) when the project has tasks. A missing/invalid token →
    /// 422 (cross-row check, like the nesting guard). <c>cascade</c> soft-deletes each task (domain method,
    /// version-bumping); <c>move_to_inbox</c> nulls <c>project_id</c> (repository set-update);
    /// <c>archive_with_tasks</c> leaves the tasks projected (the parent is archived instead of deleted).
    /// </summary>
    private static async Task ApplyTaskDispositionAsync(
        DeleteProject command,
        Domain.IdentityAccess.UserId owner,
        IProjectRepository projects,
        ITaskRepository tasks,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var taskCount = await tasks
            .CountByProjectAsync(command.Id, owner, cancellationToken)
            .ConfigureAwait(false);
        if (taskCount == 0)
        {
            return;
        }

        if (!ProjectDispositions.IsValidTaskDisposition(command.TaskDisposition))
        {
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    "taskDisposition",
                    "A task disposition is required when the project has tasks: 'cascade', 'move_to_inbox', or 'archive_with_tasks'."),
            ]);
        }

        switch (command.TaskDisposition)
        {
            case ProjectDispositions.CascadeTasks:
                var owned = await tasks.ListByProjectAsync(command.Id, owner, cancellationToken).ConfigureAwait(false);
                foreach (var task in owned)
                {
                    task.SoftDelete(utcNow);
                }

                break;

            case ProjectDispositions.MoveToInbox:
                await projects.MoveProjectTasksToInboxAsync(command.Id, owner, cancellationToken).ConfigureAwait(false);
                break;

            case ProjectDispositions.ArchiveWithTasks:
                // No task mutation — the tasks stay projected; the parent is archived (handled by the caller).
                break;

            default:
                break;
        }
    }
}
