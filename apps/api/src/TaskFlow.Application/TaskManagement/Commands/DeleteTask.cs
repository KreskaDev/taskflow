using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.TaskManagement.Events;
using Wolverine;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Application.TaskManagement.Commands;

/// <summary>
/// Soft-deletes the caller's own task by id (FR-097). VERSION-FREE and idempotent: it carries no
/// last-seen version (unlike rename/status/reorder) and never conflicts. The owner is resolved from
/// <see cref="ICurrentUser"/>, never the wire, so a caller can only ever delete a task it owns.
/// </summary>
/// <remarks>
/// This is the HTTP request bound by <c>DELETE /api/tasks/{id}</c>: <see cref="Id"/> binds from the
/// route. The endpoint returns 204 on success and on the idempotent replay.
/// </remarks>
public sealed record DeleteTask
{
    /// <summary>The client-generated UUIDv7 identity of the task to delete, carried in the route.</summary>
    public required TaskId Id { get; init; }
}

/// <summary>
/// Handles <see cref="DeleteTask"/> as a version-free, idempotent soft-delete (FR-097, research R8).
/// Authentication is enforced upstream by the deny-by-default middleware; this handler owns the
/// soft-delete + scheduled-reaper publish only.
/// </summary>
/// <remarks>
/// Decision path (owner-scoped + tombstone-INCLUSIVE load, so an own already-tombstoned row is
/// distinguishable from a foreign/absent id):
/// <list type="bullet">
/// <item>no row owned by the caller (absent OR owned by another user) → <see cref="NotFoundException"/>
/// (404, NEVER 403 — research R9/R17: the id space is not an enumeration oracle).</item>
/// <item>the caller's own row that is ALREADY soft-deleted → idempotent no-op 204: return without
/// changes and do NOT republish the reaper (the original soft-delete already scheduled one).</item>
/// <item>the caller's own LIVE row → <see cref="TaskEntity.SoftDelete"/> stamps <c>deleted_at</c> and
/// bumps the version, a SCHEDULED <see cref="ReapDeletedTask"/> is published to the outbox delayed 30s,
/// then both commit (or roll back) together in the per-message transaction.</item>
/// </list>
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-001 DeleteAccountHandler).")]
public static class DeleteTaskHandler
{
    private static readonly TimeSpan ReaperDelay = TimeSpan.FromSeconds(30);

    public static async Task Handle(
        DeleteTask command,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        IMessageContext messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(messages);

        var owner = currentUser.Id;

        // Tombstone-INCLUSIVE load: null distinguishes a foreign/absent id (→ 404) from an own
        // already-soft-deleted row (→ idempotent 204 no-op).
        var task = await tasks
            .FindOwnedIncludingDeletedAsync(command.Id, owner, cancellationToken)
            .ConfigureAwait(false);
        if (task is null)
        {
            throw new NotFoundException();
        }

        // The caller's own already-tombstoned row: idempotent no-op 204 — no changes, and do NOT
        // republish the reaper (the original soft-delete already scheduled one).
        if (task.DeletedAt is not null)
        {
            return;
        }

        // The caller's own live row: soft-delete it, then publish the SCHEDULED reaper to the outbox
        // and SaveChanges in the SAME per-message transaction (publish + EF UPDATE commit/roll back
        // together). The reaper carries the exact deleted_at instant so it stays restore-aware.
        var deletedAt = DateTime.UtcNow;
        task.SoftDelete(deletedAt);

        await messages
            .PublishAsync(
                new ReapDeletedTask(task.Id, deletedAt),
                new DeliveryOptions { ScheduleDelay = ReaperDelay })
            .ConfigureAwait(false);

        await tasks.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
