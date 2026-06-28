using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Reschedules a task's due date to a client-resolved UTC instant + <c>dueHasTime</c> flag, or clears it
/// with both null (the <c>T</c> key, AS-05, contracts/openapi.yaml <c>rescheduleTaskDueDate</c>, research
/// R4) — realizes the reschedule slice 003 deferred. Under the optimistic-concurrency <c>version</c> guard.
/// The caller is resolved from <see cref="ICurrentUser"/> — the wire NEVER supplies an owner.
/// </summary>
/// <remarks>
/// HTTP request bound by <c>PATCH /api/tasks/{id}/due-date</c>: <see cref="Id"/> binds from the route, the
/// rest from the body. The CLIENT parses the Polish phrase and resolves the instant; the SERVER re-validates
/// it (the reused slice-003 pairing/UTC-kind/range rules). Authorization is dispatched by the containing
/// project's visibility (<see cref="TaskAccessGuards.LoadWritableTaskAsync"/>).
/// </remarks>
public sealed record RescheduleDueDate
{
    /// <summary>The task identity, carried in the route.</summary>
    public required TaskId Id { get; init; }

    /// <summary>The client-resolved due-date UTC instant, or null to clear. Paired with <see cref="DueHasTime"/>.</summary>
    public required DateTime? DueDate { get; init; }

    /// <summary>The <c>has_time</c> flag, or null. Paired with <see cref="DueDate"/>.</summary>
    public required bool? DueHasTime { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token (R4).</summary>
    public required int Version { get; init; }
}

/// <summary>
/// Validates <see cref="RescheduleDueDate"/> at the boundary by reusing the slice-003 <see cref="DueDateRules"/>
/// (pairing invariant + UTC-kind guard + plausible-range sanity, research R4). A violation →
/// <c>422 validation_failed</c> (no new error code).
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors slice-002 CreateTaskValidator).")]
public sealed class RescheduleDueDateValidator : AbstractValidator<RescheduleDueDate>
{
    public RescheduleDueDateValidator()
    {
        RuleFor(x => x)
            .Must(c => DueDateRules.IsPairingConsistent(c.DueDate, c.DueHasTime))
            .WithName(nameof(RescheduleDueDate.DueDate))
            .WithMessage(DueDateRules.PairingMessage);

        RuleFor(x => x.DueDate)
            .Must(DueDateRules.IsUtcKindOrAbsent)
            .WithMessage(DueDateRules.UtcKindMessage);

        RuleFor(x => x.DueDate)
            .Must(DueDateRules.IsWithinPlausibleRange)
            .WithMessage(DueDateRules.RangeMessage);
    }
}

/// <summary>
/// Handles <see cref="RescheduleDueDate"/> under the optimistic-concurrency <c>version</c> rule (research R4).
/// Authentication is enforced upstream by the deny-by-default middleware; this handler owns the
/// dispatch-by-visibility load + version-compare + apply.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-002 RenameTaskHandler).")]
public static class RescheduleDueDateHandler
{
    public static async Task<TaskResponse> Handle(
        RescheduleDueDate command,
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

        task.Reschedule(command.DueDate, command.DueHasTime, DateTime.UtcNow);
        await tasks.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return TaskResponse.From(task);
    }
}
