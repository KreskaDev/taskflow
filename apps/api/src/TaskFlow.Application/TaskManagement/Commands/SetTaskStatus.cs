using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using DomainTaskStatus = TaskFlow.Domain.TaskManagement.TaskStatus;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Application.TaskManagement.Commands;

/// <summary>
/// The DESIRED-state status write (FR-003, slice 010 D1–D3 — renamed from <c>SetTaskDone</c>)
/// bound by <c>PATCH /api/tasks/{id}/status</c>: <see cref="Id"/> binds from the route,
/// <see cref="Status"/>/<see cref="Version"/> from the body. The request carries the desired
/// TARGET status over the full FR-003 enum plus the caller's last-seen optimistic-concurrency
/// <see cref="Version"/> — NOT a blind server-side flip — so the write is idempotent under SC-003
/// optimistic retry (two retries of one move don't cancel out).
/// </summary>
/// <remarks>
/// <see cref="Status"/> stays a string (not a domain enum) so an out-of-range target is rejected as
/// <c>422 validation_failed</c> at the FluentValidation boundary (an enum binding would surface a
/// 400 instead). All five storable statuses are accepted (D3) — the Board UI offers only the four
/// column statuses, but <c>cancelled</c> is a legitimate API value (EC-11 seeding, FR-003).
/// </remarks>
public sealed record SetTaskStatus
{
    /// <summary>The target task identity, carried in the route (FR-001).</summary>
    public required TaskId Id { get; init; }

    /// <summary>The DESIRED status (<c>backlog | todo | in_progress | done | cancelled</c>).</summary>
    public required string Status { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token (research R4).</summary>
    public required int Version { get; init; }
}

/// <summary>
/// Validates <see cref="SetTaskStatus"/> at the boundary (D3): the desired
/// <see cref="SetTaskStatus.Status"/> must be one of the five FR-003 values and
/// <see cref="SetTaskStatus.Version"/> non-negative. Any violation surfaces as
/// <c>422 validation_failed</c> via the wired Wolverine FluentValidation +
/// <c>ProblemDetailsMiddleware</c> pipeline.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors slice-002 CreateTaskValidator posture).")]
public sealed class SetTaskStatusValidator : AbstractValidator<SetTaskStatus>
{
    public SetTaskStatusValidator()
    {
        RuleFor(x => x.Status)
            .Must(status => status is "backlog" or "todo" or "in_progress" or "done" or "cancelled")
            .WithMessage("Status must be one of: backlog, todo, in_progress, done, cancelled.");
        RuleFor(x => x.Version)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Version must be non-negative.");
    }
}

/// <summary>
/// Handles <see cref="SetTaskStatus"/> as a DESIRED-state write under the optimistic-concurrency
/// <c>version</c> guard (research R3/R4). Authentication is enforced upstream by the deny-by-default
/// middleware; this handler owns the dispatch-by-visibility load + version-compare + apply logic.
/// </summary>
/// <remarks>
/// <para>Authorization is dispatched on the containing project's visibility via
/// <see cref="TaskAccessGuards.LoadWritableTaskAsync"/> with <see cref="EffectiveRole.Editor"/>
/// (unchanged from the slice-005 posture): an editor member may move a shared task, a viewer is
/// denied 403 (FR-067), a non-member/foreign id resolves 404 (FR-066 — existence undisclosed).</para>
/// Decision path:
/// <list type="bullet">
/// <item>no writable row (personal foreign/absent/soft-deleted → 404; shared non-member → 404) →
/// <see cref="NotFoundException"/>; a shared viewer → <see cref="ForbiddenException"/> (403). Checked BEFORE
/// the version compare so a foreign id is 404 for any version value.</item>
/// <item>the row exists but <c>row.Version != command.Version</c> → <see cref="VersionConflictException"/>
/// (409), rejected before any mutation.</item>
/// <item>otherwise apply the desired state through the single domain transition
/// <c>Task.SetStatus</c> (D2): entering done stamps <c>completedAt</c>, leaving done clears it,
/// a same-status request is an idempotent no-op (no version bump) — the observable desired state
/// is stable under retry either way.</item>
/// </list>
/// The interleaved-race backstop (a concurrent write between the in-memory version check and commit)
/// surfaces as a persistence <c>DbUpdateConcurrencyException</c>, which the repository translates to
/// <see cref="VersionConflictException"/> (clean-architecture dependency direction — the Application
/// layer never names an EF type), so both stale-version paths map to <c>409 version_conflict</c>.
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-002 CreateTaskHandler).")]
public static class SetTaskStatusHandler
{
    public static async Task<TaskResponse> Handle(
        SetTaskStatus command,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        IResourceAuthorizationPolicy authorization,
        Labels.ITaskLabelRepository taskLabels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(taskLabels);

        var task = await TaskAccessGuards
            .LoadWritableTaskAsync(command.Id, EffectiveRole.Editor, currentUser, tasks, projects, members, authorization, cancellationToken)
            .ConfigureAwait(false);

        if (task.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        task.SetStatus(ToDomainStatus(command.Status), DateTime.UtcNow);

        await tasks.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var labelIds = await taskLabels.ListLabelIdsForTaskAsync(task.Id, currentUser.Id, cancellationToken).ConfigureAwait(false);
        return TaskResponse.From(task, labelIds);
    }

    /// <summary>The wire→domain status map (the inverse of <c>TaskResponse.ToWireStatus</c>).</summary>
    private static DomainTaskStatus ToDomainStatus(string status) => status switch
    {
        "backlog" => DomainTaskStatus.Backlog,
        "todo" => DomainTaskStatus.Todo,
        "in_progress" => DomainTaskStatus.InProgress,
        "done" => DomainTaskStatus.Done,
        "cancelled" => DomainTaskStatus.Cancelled,
        // Unreachable: the validator rejects any other target as 422 before the handler runs.
        _ => throw new ValidationException("Status must be one of: backlog, todo, in_progress, done, cancelled."),
    };
}
