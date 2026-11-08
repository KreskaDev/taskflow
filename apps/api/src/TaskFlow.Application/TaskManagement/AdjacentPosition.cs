using TaskEntity = TaskFlow.Domain.TaskManagement.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The "directly after the source row" rank (slice 007/009 DuplicateTask D7, extracted in slice 012 D5 so the
/// recurrence successor lands in the same spot): the fractional rank strictly between the source's position and
/// its context successor's — the owner's Inbox for an unprojected task, the whole project (member-authored rows
/// included) for a projected one — or an open-ended rank after the source when nothing follows.
/// </summary>
internal static class AdjacentPosition
{
    public static async Task<string> AfterAsync(TaskEntity source, ITaskRepository tasks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tasks);

        IReadOnlyList<TaskEntity> context;
        if (source.ProjectId is { } pid)
        {
            // Project-scoped: the successor search must see MEMBER-authored rows, else the new row can land
            // on/past a member task instead of directly after its source.
            context = await tasks.ListByProjectAsync(pid, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            context = await tasks.ListOwnedAsync(source.CreatedBy, cancellationToken).ConfigureAwait(false);
        }

        var next = context
            .Select(t => t.Position)
            .Where(p => string.CompareOrdinal(p, source.Position) > 0)
            .OrderBy(p => p, StringComparer.Ordinal)
            .FirstOrDefault();

        return PositionRank.BetweenAfter(source.Position, next);
    }
}
