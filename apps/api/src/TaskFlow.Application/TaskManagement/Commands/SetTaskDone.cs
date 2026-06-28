using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Application.TaskManagement.Commands;

/// <summary>
/// The DESIRED-state toggle-done command (FR-003, research R3) bound by
/// <c>PATCH /api/tasks/{id}/status</c>: <see cref="Id"/> binds from the route,
/// <see cref="Status"/>/<see cref="Version"/> from the body. The request carries the desired
/// TARGET status (<c>done</c>|<c>backlog</c>) plus the caller's last-seen optimistic-concurrency
/// <see cref="Version"/> — NOT a blind server-side flip — so the write is idempotent under SC-003
/// optimistic retry (two retries of one Space keypress don't cancel out).
/// </summary>
/// <remarks>
/// <see cref="Status"/> stays a string (not a domain enum) so an out-of-range target is rejected as
/// <c>422 validation_failed</c> at the FluentValidation boundary (an enum binding would surface a
/// 400 instead). Only <c>done</c>|<c>backlog</c> are accepted this slice; the read model still
/// exposes the full FR-003 enum for forward-compat.
/// </remarks>
public sealed record SetTaskDone
{
    /// <summary>The target task identity, carried in the route (FR-001).</summary>
    public required TaskId Id { get; init; }

    /// <summary>The DESIRED status (<c>done</c>|<c>backlog</c> this slice).</summary>
    public required string Status { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token (research R4).</summary>
    public required int Version { get; init; }
}

/// <summary>
/// Validates <see cref="SetTaskDone"/> at the boundary (research R16): the desired
/// <see cref="SetTaskDone.Status"/> must be one of the two targets reachable this slice
/// (<c>done</c>|<c>backlog</c>). Any other value — including the storable-but-unreachable
/// <c>todo</c>/<c>in_progress</c>/<c>cancelled</c> — surfaces as <c>422 validation_failed</c> via the
/// wired Wolverine FluentValidation + <c>ProblemDetailsMiddleware</c> pipeline.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors slice-002 CreateTaskValidator posture).")]
public sealed class SetTaskDoneValidator : AbstractValidator<SetTaskDone>
{
    public SetTaskDoneValidator()
    {
        RuleFor(x => x.Status)
            .Must(status => status is "done" or "backlog")
            .WithMessage("Status must be one of: done, backlog.");
    }
}

/// <summary>
/// Handles <see cref="SetTaskDone"/> as a DESIRED-state write under the optimistic-concurrency
/// <c>version</c> guard (research R3/R4). Authentication is enforced upstream by the deny-by-default
/// middleware; this handler owns the dispatch-by-visibility load + version-compare + apply logic.
/// </summary>
/// <remarks>
/// <para>⚠ <b>slice 005 (the BLOCKER-resolved deviation, spec L127):</b> the <c>Space</c> toggle-done is now
/// <b>membership-aware</b> — authorization is dispatched on the containing project's visibility via
/// <see cref="TaskAccessGuards.LoadWritableTaskAsync"/> with <see cref="EffectiveRole.Editor"/>, exactly like
/// the three new slice-005 write commands, so an editor member MAY complete a shared task and a viewer is
/// denied 403 (FR-067: "write requires editor or owner"). The personal/Inbox path is unchanged from slice
/// 002 (foreign/absent/soft-deleted → 404, owner → allow — the slice-002 <c>SetTaskDoneTests</c> are the
/// additive-regression proof).</para>
/// Decision path:
/// <list type="bullet">
/// <item>no writable row (personal foreign/absent/soft-deleted → 404; shared non-member → 404) →
/// <see cref="NotFoundException"/>; a shared viewer → <see cref="ForbiddenException"/> (403). Checked BEFORE
/// the version compare so a foreign id is 404 for any version value.</item>
/// <item>the row exists but <c>row.Version != command.Version</c> → <see cref="VersionConflictException"/>
/// (409), rejected before any mutation.</item>
/// <item>otherwise apply the desired state: <c>done</c> → <c>MarkDone</c> (status=done, stamp
/// <c>completedAt</c>); <c>backlog</c> → <c>MarkBacklog</c> (status=backlog, clear <c>completedAt</c>).
/// Both behaviors are unconditional, so re-applying the already-current desired state still succeeds
/// (the observable target state is stable — it never toggles back).</item>
/// </list>
/// The interleaved-race backstop (a concurrent write between the in-memory version check and commit)
/// surfaces as a persistence <c>DbUpdateConcurrencyException</c>, which the repository translates to
/// <see cref="VersionConflictException"/> (clean-architecture dependency direction — the Application
/// layer never names an EF type), so both stale-version paths map to <c>409 version_conflict</c>.
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-002 CreateTaskHandler).")]
public static class SetTaskDoneHandler
{
    public static async Task<TaskResponse> Handle(
        SetTaskDone command,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        IResourceAuthorizationPolicy authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(tasks);

        var task = await TaskAccessGuards
            .LoadWritableTaskAsync(command.Id, EffectiveRole.Editor, currentUser, tasks, projects, members, authorization, cancellationToken)
            .ConfigureAwait(false);

        if (task.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        var utcNow = DateTime.UtcNow;
        switch (command.Status)
        {
            case "done":
                task.MarkDone(utcNow);
                break;
            case "backlog":
                task.MarkBacklog(utcNow);
                break;
            default:
                // Unreachable: the validator rejects any other target as 422 before the handler runs.
                throw new ValidationException("Status must be one of: done, backlog.");
        }

        await tasks.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TaskResponse.From(task);
    }
}
