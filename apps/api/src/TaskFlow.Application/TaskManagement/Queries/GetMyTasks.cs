using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;

namespace TaskFlow.Application.TaskManagement.Queries;

/// <summary>
/// Lists the calling user's own non-deleted tasks (FR-007, contracts/openapi.yaml <c>listTasks</c>).
/// Carries no wire fields — the owner is resolved from <see cref="ICurrentUser"/>, never a
/// client-supplied id, so a caller can only ever see tasks it owns (R9/R17: the list is
/// ownership-scoped, never an enumeration oracle).
/// </summary>
public sealed record GetMyTasks;

/// <summary>
/// Handles <see cref="GetMyTasks"/>. Authentication is enforced upstream by the deny-by-default
/// middleware; this handler owns only the owner-scoped read. The repository query already applies
/// <c>WHERE created_by = owner AND deleted_at IS NULL ORDER BY position, id</c>, so the handler just
/// projects each row to its lean <see cref="TaskResponse"/> wire model.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-001 GetCurrentUserHandler).")]
public static class GetMyTasksHandler
{
    public static async Task<IReadOnlyList<TaskResponse>> Handle(
        GetMyTasks query,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(tasks);

        var owned = await tasks
            .ListOwnedAsync(currentUser.Id, cancellationToken)
            .ConfigureAwait(false);

        return owned.Select(TaskResponse.From).ToList();
    }
}
