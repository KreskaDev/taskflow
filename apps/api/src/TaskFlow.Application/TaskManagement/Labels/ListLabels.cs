using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;

namespace TaskFlow.Application.TaskManagement.Labels;

/// <summary>
/// Lists the caller's labels — the roster (contracts/openapi.yaml <c>listLabels</c>, R6). Carries no wire
/// fields — the owner is resolved from <see cref="ICurrentUser"/>, never the wire (per-user isolation, FR-065).
/// </summary>
public sealed record ListLabels;

/// <summary>
/// Handles <see cref="ListLabels"/>. Authentication is enforced upstream by the deny-by-default middleware;
/// this handler owns only the owner-scoped read (ordered by name).
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors GetMyTasksHandler).")]
public static class ListLabelsHandler
{
    public static async Task<IReadOnlyList<LabelResponse>> Handle(
        ListLabels query,
        ICurrentUser currentUser,
        ILabelRepository labels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(labels);

        var owned = await labels.ListForOwnerAsync(currentUser.Id, cancellationToken).ConfigureAwait(false);
        return owned.Select(LabelResponse.From).ToList();
    }
}
