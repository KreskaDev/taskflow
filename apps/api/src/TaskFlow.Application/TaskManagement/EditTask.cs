using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The combined task editor (the <c>E</c> editor, AS-06/07/08, contracts/openapi.yaml <c>editTask</c>,
/// research R4): saves title, description, priority, due date, and project together — a WHOLE-OBJECT replace,
/// atomic on <c>Ctrl+Enter</c> — under the optimistic-concurrency <c>version</c> guard. The caller is
/// resolved from <see cref="ICurrentUser"/> — the wire NEVER supplies an owner.
/// </summary>
/// <remarks>
/// HTTP request bound by <c>PATCH /api/tasks/{id}/edit</c>: <see cref="Id"/> binds from the route, the rest
/// from the body (all fields required keys, nullable values except <see cref="Title"/>). Authorization is
/// dispatched by the containing project's visibility (<see cref="TaskAccessGuards.LoadWritableTaskAsync"/>);
/// the project field reuses the move-to-project ownership check, but ONLY on an actual move (see the handler).
/// </remarks>
public sealed record EditTask
{
    /// <summary>The task identity, carried in the route.</summary>
    public required TaskId Id { get; init; }

    /// <summary>The new title; trimmed-non-empty and ≤ 500 chars.</summary>
    public required string Title { get; init; }

    /// <summary>The new description (markdown source), or null.</summary>
    public required string? Description { get; init; }

    /// <summary>The new priority token (<c>P0</c>–<c>P3</c>), or null.</summary>
    public required string? Priority { get; init; }

    /// <summary>The new client-resolved due-date UTC instant, or null. Paired with <see cref="DueHasTime"/>.</summary>
    public required DateTime? DueDate { get; init; }

    /// <summary>The <c>has_time</c> flag, or null. Paired with <see cref="DueDate"/>.</summary>
    public required bool? DueHasTime { get; init; }

    /// <summary>The owning project, or null for the Inbox.</summary>
    public required ProjectId? ProjectId { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token (R4).</summary>
    public required int Version { get; init; }
}

/// <summary>
/// Validates <see cref="EditTask"/> at the boundary: title bounds + the closed-set priority + the reused
/// slice-003 due-date rules (<see cref="DueDateRules"/>) + description length ≤ 8000 (research R2/R3/R4). A
/// violation → <c>422 validation_failed</c> (no new error code). The whole-object-replace "omitted key →
/// 422" guarantee is provided by the <c>required</c> keys on <see cref="EditTaskRequest"/>.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors slice-002 CreateTaskValidator).")]
public sealed class EditTaskValidator : AbstractValidator<EditTask>
{
    private const int MaxTitleLength = 500;
    private const int MaxDescriptionLength = 8000;

    public EditTaskValidator()
    {
        RuleFor(x => x.Title)
            .Must(title => !string.IsNullOrWhiteSpace(title))
            .WithMessage("Title must not be empty.")
            .Must(title => title is null || title.Trim().Length <= MaxTitleLength)
            .WithMessage($"Title must be {MaxTitleLength} characters or fewer.");

        RuleFor(x => x.Description)
            .Must(description => description is null || description.Trim().Length <= MaxDescriptionLength)
            .WithMessage($"Description must be {MaxDescriptionLength} characters or fewer.");

        RuleFor(x => x.Priority)
            .Must(TaskPriority.IsValid)
            .WithMessage("Priority must be one of: P0, P1, P2, P3 (or null).");

        RuleFor(x => x)
            .Must(c => DueDateRules.IsPairingConsistent(c.DueDate, c.DueHasTime))
            .WithName(nameof(EditTask.DueDate))
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
/// Handles <see cref="EditTask"/> as a whole-object replace under the optimistic-concurrency <c>version</c>
/// rule (research R4). Authentication is enforced upstream by the deny-by-default middleware.
/// </summary>
/// <remarks>
/// Decision path:
/// <list type="bullet">
/// <item><see cref="TaskAccessGuards.LoadWritableTaskAsync"/> with <see cref="EffectiveRole.Editor"/> —
/// dispatch-by-visibility (personal foreign/absent → 404; shared viewer → 403, non-member → 404).</item>
/// <item>a stale <see cref="EditTask.Version"/> → 409, BEFORE any work.</item>
/// <item>the project field: resolve the target as OWNED (foreign/absent → 404) ONLY when it actually CHANGES
/// (<c>command.ProjectId != task.ProjectId</c>). An UNCHANGED <c>projectId</c> is not a move and skips the
/// check — otherwise an editor editing a shared task they do not own would be spuriously 404'd by the
/// owner-scoped target check. A null target (Inbox) is always allowed. The changed-target case keeps the
/// owner-scoped check, so an editor may move a task only into the Inbox or their own projects.</item>
/// <item>otherwise <c>Task.EditTask</c> (one Touch) and persist; the interleaved-race backstop is closed at
/// <c>TaskRepository.SaveChangesAsync</c> (→ 409).</item>
/// </list>
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-002 RenameTaskHandler).")]
public static class EditTaskHandler
{
    public static async Task<TaskResponse> Handle(
        EditTask command,
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
        ArgumentNullException.ThrowIfNull(projects);

        var task = await TaskAccessGuards
            .LoadWritableTaskAsync(command.Id, EffectiveRole.Editor, currentUser, tasks, projects, members, authorization, cancellationToken)
            .ConfigureAwait(false);

        if (task.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        // The project field is checked ONLY on an actual move (R4 + the membership-arm fix): a non-null target
        // that DIFFERS from the current project must be a project the caller OWNS (foreign/absent → 404), so a
        // task can never be filed under another user's project. An UNCHANGED projectId is not a move and skips
        // this — so an editor editing a shared task they do not own is not spuriously 404'd. A null target
        // (Inbox) needs no ownership check.
        if (command.ProjectId != task.ProjectId && command.ProjectId is { } targetProjectId)
        {
            var target = await projects
                .FindOwnedAsync(targetProjectId, currentUser.Id, cancellationToken)
                .ConfigureAwait(false);
            if (target is null)
            {
                throw new NotFoundException();
            }
        }

        task.EditTask(
            command.Title, command.Description, command.Priority,
            command.DueDate, command.DueHasTime, command.ProjectId, DateTime.UtcNow);
        await tasks.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return TaskResponse.From(task);
    }
}
