using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Application.TaskManagement.Commands;

/// <summary>
/// Moves the caller's task to a new client-computed fractional rank (FR-102, research R5). The
/// server is the SOLE writer of <c>position</c> under the optimistic-concurrency <c>version</c>
/// guard — it validates the rank FORMAT only and NEVER generates ranks (the client is the sole rank
/// generator; on a 409 it refetches and recomputes the rank from fresh neighbours).
/// </summary>
/// <remarks>
/// This is the HTTP request bound by <c>PATCH /api/tasks/{id}/position</c>: <see cref="Id"/> binds
/// from the route, <see cref="Position"/>/<see cref="Version"/> from the body. The owner is resolved
/// from <see cref="ICurrentUser"/> inside the handler — never the wire — so a caller can only ever
/// reorder a task it owns.
/// </remarks>
public sealed record ReorderTask
{
    /// <summary>The target task's identity, carried in the route.</summary>
    public required TaskId Id { get; init; }

    /// <summary>The client-computed fractional-indexing rank string the task moves to (R5).</summary>
    public required string Position { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token (R4); a stale value → 409.</summary>
    public required int Version { get; init; }
}

/// <summary>
/// Validates <see cref="ReorderTask"/> at the boundary (research R16): <see cref="ReorderTask.Position"/>
/// must be a non-empty, well-formed fractional-indexing rank — enforced via the SHARED
/// <see cref="PositionRank"/> rule reused by create (one identical FORMAT definition, never duplicated).
/// A violation surfaces as <c>422 validation_failed</c> via the wired Wolverine FluentValidation +
/// <c>ProblemDetailsMiddleware</c> pipeline.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors slice-002 CreateTaskValidator posture).")]
public sealed class ReorderTaskValidator : AbstractValidator<ReorderTask>
{
    public ReorderTaskValidator()
    {
        RuleFor(x => x.Position).ValidPositionRank();
    }
}

/// <summary>
/// Handles <see cref="ReorderTask"/> as a normal optimistic write (research R4/R17). Authentication is
/// enforced upstream by the deny-by-default middleware; this handler owns the owner-scoped load, the
/// version compare, and the single <c>position</c> write.
/// </summary>
/// <remarks>
/// Decision path (owner-scoped + NON-deleted load, so a foreign/absent/soft-deleted id is
/// indistinguishable — no enumeration oracle):
/// <list type="bullet">
/// <item>no live row owned by the caller → <see cref="NotFoundException"/> (404), NEVER 403 (R17).</item>
/// <item>the loaded row's <c>version</c> ≠ the caller's last-seen version → <see cref="VersionConflictException"/>
/// (409) BEFORE any write — the common stale-token case.</item>
/// <item>otherwise call <c>task.Reorder(position, utcNow)</c> — the sole writer of <c>position</c>, which
/// bumps <c>version</c> + stamps <c>updated_at</c> — then persist. An interleaved race that slips past the
/// in-handler compare is caught by the EF concurrency token on <c>version</c>: the repository translates
/// that <c>DbUpdateConcurrencyException</c> into a <see cref="VersionConflictException"/> (409) backstop.</item>
/// </list>
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-002 CreateTaskHandler).")]
public static class ReorderTaskHandler
{
    public static async Task<TaskResponse> Handle(
        ReorderTask command,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(tasks);

        var task = await tasks
            .FindOwnedAsync(command.Id, currentUser.Id, cancellationToken)
            .ConfigureAwait(false);
        if (task is null)
        {
            // Foreign / absent / soft-deleted id all resolve to 404 (R17) — never 403.
            throw new NotFoundException();
        }

        if (task.Version != command.Version)
        {
            // Stale optimistic-concurrency token: reject BEFORE writing (the common 409 case).
            throw new VersionConflictException();
        }

        // Sole writer of `position`: bumps `version` + stamps `updated_at`.
        task.Reorder(command.Position, DateTime.UtcNow);

        // The interleaved-race backstop: an EF DbUpdateConcurrencyException (the `version` concurrency
        // token no longer matches) is translated by the repository into a VersionConflictException (409).
        await tasks.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return TaskResponse.From(task);
    }
}
