using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using LabelId = TaskFlow.Domain.TaskManagement.LabelId;

namespace TaskFlow.Application.TaskManagement.Labels;

/// <summary>
/// Hard-deletes the caller's label (R3) — labels are not in the FR-040/FR-097 task/project undo scope. The
/// <c>task_labels.label_id</c> FK cascade removes every application (the label vanishes from every task it was
/// on). Ownership-gated (not-owned/absent → 404). Returns 204.
/// </summary>
public sealed record DeleteLabel
{
    /// <summary>The label identity, carried in the route.</summary>
    public required LabelId Id { get; init; }
}

/// <summary>
/// Handles <see cref="DeleteLabel"/> (R3/R4). Authentication is enforced upstream by the deny-by-default
/// middleware; this handler owns the ownership-404 + the hard delete (the FK cascade clears applications).
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors DeleteTaskHandler).")]
public static class DeleteLabelHandler
{
    public static async Task Handle(
        DeleteLabel command,
        ICurrentUser currentUser,
        ILabelRepository labels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(labels);

        var label = await labels.FindOwnedAsync(command.Id, currentUser.Id, cancellationToken).ConfigureAwait(false);
        if (label is null)
        {
            throw new NotFoundException();
        }

        labels.Remove(label);
        await labels.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
