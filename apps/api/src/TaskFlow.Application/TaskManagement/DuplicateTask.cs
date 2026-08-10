using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.IdentityAccess;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Duplicate a task into its own context (slice 019, FR-112; contracts/task-duplicate.md).
/// Copies the user-editable fields — title, description, priority, due date (incl.
/// <c>has_time</c>), project assignment, labels (the CALLER's), and assignees re-validated
/// against CURRENT membership (non-members silently dropped, no notifications re-fired) —
/// but NOT completion state and NOT comments. The duplicate starts at the FR-003 default
/// with fresh timestamps and a position directly AFTER the source (D7 adjacency).
/// </summary>
public sealed record DuplicateTask
{
    /// <summary>The source task id, carried in the route.</summary>
    public required TaskId SourceId { get; init; }

    /// <summary>The client-generated id of the duplicate (idempotency + optimistic paint with a known id).</summary>
    public required TaskId NewTaskId { get; init; }
}

/// <summary>
/// Handles <see cref="DuplicateTask"/>. Authorization = the same scoping as task creation in the
/// source's context (FR-065/FR-068): personal/Inbox → ownership (foreign → 404); shared project →
/// current membership at Editor+ (viewer → 403, non-member → 404) — enforced by
/// <see cref="TaskAccessGuards.LoadWritableTaskAsync"/>.
/// </summary>
/// <remarks>
/// Identity semantics (contracts/task-duplicate.md):
/// <list type="bullet">
/// <item>replay with the same <c>newTaskId</c> whose row matches the source context (same title +
/// project, caller-owned, live) → the existing duplicate, unchanged (no second copy);</item>
/// <item><c>newTaskId</c> already taken by ANY other row (caller's unrelated task, a foreign task,
/// or the caller's spent tombstone) → <see cref="DuplicateTaskIdException"/> → 409 <c>duplicate_id</c>;</item>
/// <item>otherwise insert; the PK is the race backstop and a concurrent duplicate re-resolves
/// through the same decision path.</item>
/// </list>
/// The copied assignees raise NO outward effect: the <c>TaskAssigned</c> domain event is cleared
/// before save (carry-forward must not re-fire assignment notifications — data-model.md).
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors CreateTaskHandler).")]
public static class DuplicateTaskHandler
{
    public static async Task<TaskResponse> Handle(
        DuplicateTask command,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        Labels.ITaskLabelRepository taskLabels,
        IResourceAuthorizationPolicy authorization,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(taskLabels);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var caller = currentUser.Id;

        // 1. Authorize against the SOURCE's context — the same gate task creation uses there
        //    (personal foreign → 404; shared viewer → 403; non-member → 404).
        var source = await TaskAccessGuards
            .LoadWritableTaskAsync(command.SourceId, EffectiveRole.Editor, currentUser, tasks, projects, members, authorization, cancellationToken)
            .ConfigureAwait(false);

        var callerSourceLabels = await taskLabels
            .ListLabelIdsForTaskAsync(source.Id, caller, cancellationToken)
            .ConfigureAwait(false);

        // 2. Idempotency / collision resolution on the new id.
        var existing = await tasks
            .FindByIdIncludingDeletedAsync(command.NewTaskId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return await ResolveExistingAsync(existing).ConfigureAwait(false);
        }

        // 3. Adjacency (D7): position directly after the source, before its context successor.
        var successorPosition = await FindSuccessorPositionAsync().ConfigureAwait(false);
        var position = PositionRank.BetweenAfter(source.Position, successorPosition);

        // 4. Assemble the duplicate: fresh identity/status/timestamps, copied editable fields.
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var duplicate = TaskEntity.Create(command.NewTaskId, caller, source.Title, position, now);
        duplicate.EditTask(source.Title, source.Description, source.Priority, source.DueDate, source.DueHasTime, source.ProjectId, now);

        if (source.ProjectId is { } projectId && source.Assignees.Count > 0)
        {
            // Re-validate the copied assignees against CURRENT membership (∪ the owner anchor);
            // non-members are silently dropped (data-model.md — the FR-008 carry-forward precedent).
            var memberships = await members.ListByProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
            var project = await projects.FindReadableAsync(projectId, caller, cancellationToken).ConfigureAwait(false)
                ?? throw new NotFoundException();
            var validAssignees = new HashSet<UserId>(memberships.Select(m => m.UserId)) { project.OwnerId };

            var carried = source.Assignees
                .Select(a => a.UserId)
                .Where(validAssignees.Contains)
                .ToList();
            if (carried.Count > 0)
            {
                duplicate.SetAssignees(carried, actor: caller, now);
            }
        }

        // Copying assignees must NOT re-fire assignment notifications (contract): drop the
        // TaskAssigned event the domain method recorded before the dispatch drain sees it.
        duplicate.ClearDomainEvents();

        tasks.Add(duplicate);
        try
        {
            await tasks.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateTaskIdException)
        {
            // A concurrent insert took the id — re-resolve through the same decision path.
            var resolved = await tasks
                .FindByIdIncludingDeletedAsync(command.NewTaskId, cancellationToken)
                .ConfigureAwait(false);
            if (resolved is null)
            {
                throw;
            }

            return await ResolveExistingAsync(resolved).ConfigureAwait(false);
        }

        // 5. The caller's labels on the source copy to the duplicate (per-user relation, slice 006).
        if (callerSourceLabels.Count > 0)
        {
            await taskLabels
                .SetForOwnerAsync(duplicate.Id, caller, callerSourceLabels.Select(TaskFlow.Domain.TaskManagement.LabelId.From).ToList(), cancellationToken)
                .ConfigureAwait(false);
        }

        return TaskResponse.From(duplicate, callerSourceLabels);

        // Replay iff the row is the caller's LIVE duplicate of THIS source (same title + context);
        // anything else holding the id is a conflict (409 duplicate_id — the id is not reusable).
        async Task<TaskResponse> ResolveExistingAsync(TaskEntity row)
        {
            var isReplay = row.DeletedAt is null
                && row.CreatedBy == caller
                && string.Equals(row.Title, source.Title, StringComparison.Ordinal)
                && row.ProjectId == source.ProjectId;
            if (!isReplay)
            {
                throw new DuplicateTaskIdException();
            }

            var labels = await taskLabels
                .ListLabelIdsForTaskAsync(row.Id, caller, cancellationToken)
                .ConfigureAwait(false);
            return TaskResponse.From(row, labels);
        }

        async Task<string?> FindSuccessorPositionAsync()
        {
            IReadOnlyList<TaskEntity> context;
            if (source.ProjectId is { } pid)
            {
                // Project-scoped (D4): the successor search must see MEMBER-authored rows, else the
                // duplicate can land on/past a member task instead of directly after its source.
                context = await tasks.ListByProjectAsync(pid, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                context = await tasks.ListOwnedAsync(caller, cancellationToken).ConfigureAwait(false);
            }

            return context
                .Select(t => t.Position)
                .Where(p => string.CompareOrdinal(p, source.Position) > 0)
                .OrderBy(p => p, StringComparer.Ordinal)
                .FirstOrDefault();
        }
    }
}
