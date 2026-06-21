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

    /// <summary>
    /// The resolved due-date instant in UTC (FR-092, R8), or null for no due date. Paired with
    /// <see cref="DueHasTime"/> — both null or both non-null (validated by <see cref="CreateTaskValidator"/>).
    /// </summary>
    public DateTime? DueDate { get; init; }

    /// <summary>
    /// The <c>DueDate.has_time</c> flag (R2/R8), or null for no due date. Paired with
    /// <see cref="DueDate"/> — both null or both non-null (validated by <see cref="CreateTaskValidator"/>).
    /// </summary>
    public bool? DueHasTime { get; init; }
}

/// <summary>
/// Validates <see cref="CreateTask"/> at the boundary (research R16): <see cref="CreateTask.Title"/>
/// trimmed-non-empty and ≤ 500 chars, and <see cref="CreateTask.Position"/> a non-empty, well-formed
/// fractional-indexing rank (the shared <see cref="PositionRank"/> rule, reused by reorder). The due
/// date (slice 003) adds three trust-boundary rules (R8/R11/R13): the <c>{DueDate, DueHasTime}</c>
/// pairing invariant (both null or both non-null), a UTC-kind guard (a non-<c>Z</c> instant would make
/// Npgsql throw an unhandled 500 against the <c>timestamptz</c> column — rejected as 422 instead), and a
/// wide plausible-range sanity window (a zone-agnostic UTC comparison — no NodaTime this slice). A
/// violation surfaces as <c>422 validation_failed</c> via the wired Wolverine FluentValidation +
/// <c>ProblemDetailsMiddleware</c> pipeline (no new error code).
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors slice-001 public handler/DTO posture).")]
public sealed class CreateTaskValidator : AbstractValidator<CreateTask>
{
    private const int MaxTitleLength = 500;

    /// <summary>The earliest plausible due-date instant — a wide sanity floor, not business logic (R11).</summary>
    private static readonly DateTime MinDue = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The plausible-range horizon: ~10 years beyond now (R11). A UTC comparison, zone-agnostic.</summary>
    private const int MaxDueYearsAhead = 10;

    public CreateTaskValidator()
    {
        RuleFor(x => x.Title)
            .Must(title => !string.IsNullOrWhiteSpace(title))
            .WithMessage("Title must not be empty.")
            .Must(title => title is null || title.Trim().Length <= MaxTitleLength)
            .WithMessage($"Title must be {MaxTitleLength} characters or fewer.");

        RuleFor(x => x.Position).ValidPositionRank();

        // Pairing invariant (R8): both null (no due date) or both non-null. A PRESENCE check —
        // DueHasTime=false is "present" (date-only), so compare HasValue, never truthiness.
        RuleFor(x => x)
            .Must(c => c.DueDate.HasValue == c.DueHasTime.HasValue)
            .WithName(nameof(CreateTask.DueDate))
            .WithMessage("DueDate and DueHasTime must be set together (both present or both absent).");

        // UTC-kind guard (R13): the resolved instant MUST be a Z-form UTC DateTime, else 422 — a
        // non-UTC kind would make Npgsql throw an unhandled 500 writing to the timestamptz column.
        RuleFor(x => x.DueDate)
            .Must(due => !due.HasValue || due.Value.Kind == DateTimeKind.Utc)
            .WithMessage("DueDate must be a UTC instant (an ISO-8601 'Z' form).");

        // Plausible-range sanity window (R11): reject corrupt/absurd instants. Zone-agnostic UTC compare.
        RuleFor(x => x.DueDate)
            .Must(BeWithinPlausibleRange)
            .WithMessage("DueDate is outside the plausible range.");
    }

    private static bool BeWithinPlausibleRange(DateTime? due)
    {
        if (!due.HasValue || due.Value.Kind != DateTimeKind.Utc)
        {
            // No due date (passes) or wrong kind (the UTC-kind rule owns that failure — don't double-fail
            // on a kind we can't meaningfully range-compare).
            return true;
        }

        return due.Value >= MinDue && due.Value <= DateTime.UtcNow.AddYears(MaxDueYearsAhead);
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
        var created = TaskEntity.Create(
            command.Id, owner, command.Title, command.Position, DateTime.UtcNow,
            command.DueDate, command.DueHasTime);
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
