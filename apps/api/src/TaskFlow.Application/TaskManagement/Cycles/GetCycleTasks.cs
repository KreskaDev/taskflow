using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;

namespace TaskFlow.Application.TaskManagement.Cycles;

/// <summary>
/// A cycle's task rows FILTERED to the caller's visibility (slice 011, contracts/cycles-api.md
/// <c>getCycleTasks</c>, D10/FR-065): own personal tasks + tasks of projects the caller can read
/// (current membership ∪ owned shared). Serves the Cycle view list and the close review. EC-12:
/// tasks of an ARCHIVED project remain in these rows (hidden from project views, visible here).
/// </summary>
public sealed record GetCycleTasks
{
    /// <summary>The cycle identity, carried in the route (404 when unknown).</summary>
    public required Domain.TaskManagement.CycleId Id { get; init; }
}

/// <summary>
/// Handles <see cref="GetCycleTasks"/>: existence check (404) → team-wide row load → the FR-065
/// visibility dispatch applied in-memory (the assigned-view precedent; ASM-10 scale) → the
/// caller-scoped label join threaded into <see cref="TaskResponse"/>. Ordered by position.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen.")]
public static class GetCycleTasksHandler
{
    public static async Task<IReadOnlyList<TaskResponse>> Handle(
        GetCycleTasks query,
        ICurrentUser currentUser,
        ICycleRepository cycles,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        Labels.ITaskLabelRepository taskLabels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(cycles);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(taskLabels);

        var cycle = await cycles.FindByIdAsync(query.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException();

        var rows = await cycles.ListTasksInCycleAsync(cycle.Id.Value, cancellationToken).ConfigureAwait(false);

        var readableProjects = await CycleTaskVisibility
            .ListReadableProjectIdsAsync(currentUser, projects, members, cancellationToken)
            .ConfigureAwait(false);

        var visible = rows
            .Where(t => CycleTaskVisibility.IsVisible(t, currentUser.Id, readableProjects))
            .ToList();

        var labelsByTask = await taskLabels
            .ListLabelIdsForTasksAsync(visible.Select(t => t.Id).ToList(), currentUser.Id, cancellationToken)
            .ConfigureAwait(false);

        return visible
            .Select(t => TaskResponse.From(
                t,
                labelsByTask.TryGetValue(t.Id, out var ids) ? ids : []))
            .ToList();
    }
}
