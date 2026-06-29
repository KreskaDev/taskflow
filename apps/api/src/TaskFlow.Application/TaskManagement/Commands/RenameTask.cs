using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.IdentityAccess;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Application.TaskManagement.Commands;

/// <summary>
/// Renames the caller's own task (FR-001, US2, contracts/openapi.yaml <c>renameTask</c>) under the
/// optimistic-concurrency <c>version</c> guard (research R4). The caller is resolved from
/// <see cref="ICurrentUser"/> — the wire NEVER supplies an owner, so a caller can only ever rename a
/// task it owns.
/// </summary>
/// <remarks>
/// This is the HTTP request bound by <c>PATCH /api/tasks/{id}/title</c>: <see cref="Id"/> binds from
/// the route, <see cref="Title"/>/<see cref="Version"/> from the body. <see cref="Version"/> is the
/// caller's last-seen optimistic-concurrency token (R4) — a stale value is rejected with
/// <c>409 version_conflict</c> before the rename is applied.
/// </remarks>
public sealed record RenameTask
{
    /// <summary>The task identity, carried in the route.</summary>
    public required TaskId Id { get; init; }

    /// <summary>The new task title; trimmed-non-empty and ≤ 500 chars (FR-001).</summary>
    public required string Title { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token (R4).</summary>
    public required int Version { get; init; }
}

/// <summary>
/// Validates <see cref="RenameTask"/> at the boundary (research R16): <see cref="RenameTask.Title"/>
/// trimmed-non-empty and ≤ 500 chars (copied verbatim from <see cref="CreateTaskValidator"/> so both
/// trust boundaries stay in lockstep — the trim-then-length form guarantees a >500 char title fails
/// validation as <c>422</c> BEFORE the domain <c>NormalizeTitle</c> guard could throw a 500). A
/// violation surfaces as <c>422 validation_failed</c> via the wired Wolverine FluentValidation +
/// <c>ProblemDetailsMiddleware</c> pipeline.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors slice-002 CreateTaskValidator).")]
public sealed class RenameTaskValidator : AbstractValidator<RenameTask>
{
    private const int MaxTitleLength = 500;

    public RenameTaskValidator()
    {
        RuleFor(x => x.Title)
            .Must(title => !string.IsNullOrWhiteSpace(title))
            .WithMessage("Title must not be empty.")
            .Must(title => title is null || title.Trim().Length <= MaxTitleLength)
            .WithMessage($"Title must be {MaxTitleLength} characters or fewer.");
    }
}

/// <summary>
/// Handles <see cref="RenameTask"/> under the optimistic-concurrency <c>version</c> rule (research R4).
/// Authentication is enforced upstream by the deny-by-default middleware; this handler owns the
/// owner-scoped load + version-compare + rename only.
/// </summary>
/// <remarks>
/// Decision path:
/// <list type="bullet">
/// <item>owner-scoped + NON-deleted load (<see cref="ITaskRepository.FindOwnedAsync"/>); a foreign,
/// absent, or soft-deleted id all resolve to <c>null</c> → <see cref="NotFoundException"/> (404,
/// NEVER 403 — research R9/R17: the id space is not an enumeration oracle).</item>
/// <item>the caller's last-seen <see cref="RenameTask.Version"/> no longer matches the stored row →
/// <see cref="VersionConflictException"/> (409 <c>version_conflict</c>), applied BEFORE the rename so a
/// rejected request leaves the row untouched.</item>
/// <item>otherwise call <c>Task.Rename</c> (which bumps <c>Version</c> + stamps <c>UpdatedAt</c>) and
/// persist. The interleaved-race backstop — a concurrent write that changes the version between this
/// compare and the commit — is closed at the persistence seam: <c>TaskRepository.SaveChangesAsync</c>
/// translates EF's <c>DbUpdateConcurrencyException</c> into <see cref="VersionConflictException"/>
/// (→ 409), so this handler stays free of any EF dependency and needs no try/catch.</item>
/// </list>
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-002 CreateTaskHandler).")]
public static class RenameTaskHandler
{
    public static async Task<TaskResponse> Handle(
        RenameTask command,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        Labels.ITaskLabelRepository taskLabels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(taskLabels);

        var owner = currentUser.Id;

        var task = await tasks
            .FindOwnedAsync(command.Id, owner, cancellationToken)
            .ConfigureAwait(false);
        if (task is null)
        {
            throw new NotFoundException();
        }

        if (task.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        task.Rename(command.Title, DateTime.UtcNow);
        await tasks.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var labelIds = await taskLabels.ListLabelIdsForTaskAsync(task.Id, owner, cancellationToken).ConfigureAwait(false);
        return TaskResponse.From(task, labelIds);
    }
}
