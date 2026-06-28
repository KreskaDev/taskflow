using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.IdentityAccess;
using Wolverine;
using DomainProject = TaskFlow.Domain.TaskManagement.Project;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Replaces a shared-project task's assignee set (the assignee picker, AS-01/AS-02, contracts/openapi.yaml
/// <c>setTaskAssignees</c>, research R2/R4) under the optimistic-concurrency <c>version</c> guard. The caller
/// is resolved from <see cref="ICurrentUser"/> — the wire NEVER supplies an actor.
/// </summary>
/// <remarks>
/// HTTP request bound by <c>PATCH /api/tasks/{id}/assignees</c>: <see cref="Id"/> from the route, the rest
/// from the body. WHOLE-SET replace; the handler computes the delta, raises one idempotent
/// <c>TaskAssigned</c> event (slice 017), and enforces: shared-only (personal/Inbox → 404), editor/owner
/// (viewer → 403, non-member → 404), and assignee-must-be-a-current-member (non-member assignee → 422).
/// </remarks>
public sealed record SetTaskAssignees
{
    /// <summary>The task identity, carried in the route.</summary>
    public required TaskId Id { get; init; }

    /// <summary>The desired full assignee set.</summary>
    public required IReadOnlyList<Guid> AssigneeIds { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token (R4).</summary>
    public required int Version { get; init; }
}

/// <summary>
/// Validates <see cref="SetTaskAssignees"/> at the boundary (research R2): the assignee set has no
/// duplicates and a sane cardinality cap. The cross-row member-validity check lives in the handler (it needs
/// the project's membership set). A violation → <c>422 validation_failed</c>.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors slice-002 CreateTaskValidator).")]
public sealed class SetTaskAssigneesValidator : AbstractValidator<SetTaskAssignees>
{
    private const int MaxAssignees = 50; // a sane cap for a ~10-person team's shared project (ASM-10).

    public SetTaskAssigneesValidator()
    {
        RuleFor(x => x.AssigneeIds)
            .NotNull()
            .Must(ids => ids is null || ids.Count <= MaxAssignees)
            .WithMessage($"A task may have at most {MaxAssignees} assignees.")
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count)
            .WithMessage("Assignee ids must not contain duplicates.");
    }
}

/// <summary>
/// Handles <see cref="SetTaskAssignees"/> (research R2/R3/R4). Authentication is enforced upstream by the
/// deny-by-default middleware; this handler owns the dispatch-by-visibility load + shared-only + version +
/// member-validity + apply + event drain.
/// </summary>
/// <remarks>
/// Decision path:
/// <list type="bullet">
/// <item><see cref="TaskAccessGuards.LoadWritableTaskAsync"/> with <see cref="EffectiveRole.Editor"/>
/// (personal foreign → 404; shared viewer → 403, non-member → 404).</item>
/// <item>shared-only: a personal/Inbox task → <see cref="NotFoundException"/> (no assignment surface, FR-069
/// — mirrors the slice-007 "/members exists only on a shared project" posture).</item>
/// <item>a stale <see cref="SetTaskAssignees.Version"/> → 409, before any work.</item>
/// <item>member-validity: every id MUST be a current member (the membership set ∪ the owner anchor) — else
/// <see cref="ValidationException"/> → 422, no assignee added.</item>
/// <item><c>Task.SetAssignees</c> (delta + one <c>TaskAssigned</c> on a real change; idempotent no-op
/// otherwise), drain the event to the outbox, persist (the version backstop → 409).</item>
/// </list>
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-005 SetPriorityHandler).")]
public static class SetTaskAssigneesHandler
{
    public static async Task<TaskResponse> Handle(
        SetTaskAssignees command,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        IResourceAuthorizationPolicy authorization,
        IMessageContext messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(messages);

        // Base dispatch-by-visibility (foreign → 404, viewer → 403, non-member → 404).
        var task = await TaskAccessGuards
            .LoadWritableTaskAsync(command.Id, EffectiveRole.Editor, currentUser, tasks, projects, members, authorization, cancellationToken)
            .ConfigureAwait(false);

        // Shared-only: assignment exists only on a shared-project task (FR-069). An Inbox/personal task has
        // no assignment surface → 404 (existence-of-surface posture).
        if (task.ProjectId is not { } projectId)
        {
            throw new NotFoundException();
        }

        var project = await projects.FindReadableAsync(projectId, currentUser.Id, cancellationToken).ConfigureAwait(false);
        if (project is null || project.Visibility != DomainProject.SharedVisibility)
        {
            throw new NotFoundException();
        }

        if (task.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        // Member-validity: every assignee MUST be a current member (the membership rows ∪ the owner anchor).
        var memberships = await members.ListByProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        var memberIds = memberships.Select(m => m.UserId).Append(project.OwnerId).ToHashSet();
        var desired = command.AssigneeIds.Select(UserId.From).ToList();
        if (desired.Any(id => !memberIds.Contains(id)))
        {
            throw new ValidationException("Every assignee must be a current member of the project.");
        }

        task.SetAssignees(desired, currentUser.Id, DateTime.UtcNow);
        await DomainEventDispatch.PublishAndClearAsync(task, messages, cancellationToken).ConfigureAwait(false);
        await tasks.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return TaskResponse.From(task);
    }
}
