using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.IdentityAccess;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Idempotent insert-if-not-exists by the client-generated id (FR-001, research R2). The
/// caller is resolved from <see cref="ICurrentUser"/> — the wire NEVER supplies a
/// <c>createdBy</c>, so a caller can only ever create a task it owns.
/// </summary>
/// <remarks>
/// This is the HTTP request bound by <c>PUT /api/tasks/{id}</c>: <see cref="Id"/> binds from
/// the route, <see cref="Title"/>/<see cref="Position"/> from the body. <see cref="Position"/>
/// is client-authoritative (a fractional-indexing rank string, R5) — the server validates its
/// format but never generates ranks.
/// </remarks>
public sealed record CreateTask
{
    /// <summary>The client-generated UUIDv7 identity, carried in the route (FR-001).</summary>
    public required TaskId Id { get; init; }

    /// <summary>The task title; trimmed-non-empty and ≤ 500 chars (FR-001).</summary>
    public required string Title { get; init; }

    /// <summary>The client-computed fractional-indexing rank string (R5).</summary>
    public required string Position { get; init; }
}

/// <summary>
/// Validates <see cref="CreateTask"/> at the boundary (research R16): <see cref="CreateTask.Title"/>
/// trimmed-non-empty and ≤ 500 chars, and <see cref="CreateTask.Position"/> a non-empty, well-formed
/// fractional-indexing rank (the shared <see cref="PositionRank"/> rule, reused by reorder). A
/// violation surfaces as <c>422 validation_failed</c> via the wired Wolverine FluentValidation +
/// <c>ProblemDetailsMiddleware</c> pipeline.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors slice-001 public handler/DTO posture).")]
public sealed class CreateTaskValidator : AbstractValidator<CreateTask>
{
    private const int MaxTitleLength = 500;

    public CreateTaskValidator()
    {
        RuleFor(x => x.Title)
            .Must(title => !string.IsNullOrWhiteSpace(title))
            .WithMessage("Title must not be empty.")
            .Must(title => title is null || title.Trim().Length <= MaxTitleLength)
            .WithMessage($"Title must be {MaxTitleLength} characters or fewer.");

        RuleFor(x => x.Position).ValidPositionRank();
    }
}

/// <summary>
/// Handles <see cref="CreateTask"/> as insert-if-not-exists keyed on the client id (research R2).
/// Authentication is enforced upstream by the deny-by-default middleware; this handler owns the
/// idempotent-create + ownership-disclosure logic only.
/// </summary>
/// <remarks>
/// Decision path (owner-scoped + tombstone-INCLUSIVE load, so a foreign or reused-tombstoned id is
/// distinguishable from a fresh one):
/// <list type="bullet">
/// <item>a live row owned by the caller exists → idempotent replay: return it UNCHANGED (create is
/// NOT a replace — title/position/version are untouched; all edits go through the dedicated
/// rename/status/position commands under the <c>version</c> rule).</item>
/// <item>a row exists but is the caller's own tombstone → the id is spent → <see cref="NotFoundException"/>
/// (404); recreate uses a fresh id.</item>
/// <item>no row for the caller (absent OR owned by another user) → attempt the insert; the DB primary
/// key is the race backstop, so a concurrent double-insert surfaces as <see cref="DuplicateTaskIdException"/>
/// which is re-resolved through the SAME find-then-decide path (a still-foreign id then resolves to 404,
/// NOT a re-insert).</item>
/// </list>
/// Posture (R17): a foreign / absent / soft-deleted id resolves to 404, never 403 — the id space is
/// not an enumeration oracle.
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-001 EnsureUserHandler).")]
public static class CreateTaskHandler
{
    public static async Task<TaskResponse> Handle(
        CreateTask command,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(tasks);

        var owner = currentUser.Id;

        var existing = await tasks
            .FindOwnedIncludingDeletedAsync(command.Id, owner, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return Resolve(existing);
        }

        // No row for this caller (absent, or owned by another user). Attempt the insert; the PK on
        // `id` is the race backstop for a concurrent same-id insert.
        var created = TaskEntity.Create(command.Id, owner, command.Title, command.Position, DateTime.UtcNow);
        tasks.Add(created);
        try
        {
            await tasks.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return TaskResponse.From(created);
        }
        catch (DuplicateTaskIdException)
        {
            // A concurrent insert (or a foreign id) holds the PK. Re-resolve through the SAME
            // find-then-decide path: own live row → idempotent hit; own tombstone or still-foreign
            // (re-resolve is null) → 404. NEVER re-insert.
            var resolved = await tasks
                .FindOwnedIncludingDeletedAsync(command.Id, owner, cancellationToken)
                .ConfigureAwait(false);
            if (resolved is null)
            {
                throw new NotFoundException();
            }

            return Resolve(resolved);
        }
    }

    private static TaskResponse Resolve(TaskEntity existing)
    {
        // The id is the caller's own already-soft-deleted row: the id is spent, treat as not-found.
        if (existing.DeletedAt is not null)
        {
            throw new NotFoundException();
        }

        // Idempotent replay: return the existing row UNCHANGED (no overwrite, no version bump).
        return TaskResponse.From(existing);
    }
}
