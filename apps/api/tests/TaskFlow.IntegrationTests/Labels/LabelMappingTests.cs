using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Application.TaskManagement.Labels;
using TaskFlow.IntegrationTests.Infrastructure;
using TaskFlow.Infrastructure.Persistence;
using Label = TaskFlow.Domain.TaskManagement.Label;
using LabelId = TaskFlow.Domain.TaskManagement.LabelId;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;
using TaskLabel = TaskFlow.Domain.TaskManagement.TaskLabel;

namespace TaskFlow.IntegrationTests.Labels;

/// <summary>
/// The EF-mapping spike (slice 006): proves the two risky persistence pieces against real PostgreSQL —
/// (a) a standalone entity (<see cref="TaskLabel"/>) with a composite value-converted <c>(task_id, label_id)</c>
/// key persists and round-trips; (b) the caller-scoped batched join
/// (<see cref="ITaskLabelRepository.ListLabelIdsForTasksAsync"/>) TRANSLATES with no Npgsql
/// value-converted-id-in-collection error (the non-nullable case, distinct from the slice-005 nullable-FK trap).
/// </summary>
public sealed class LabelMappingTests : SharingTestBase
{
    [Fact]
    public async Task TaskLabel_composite_value_converted_key_round_trips()
    {
        var user = await CreateUserAsync("g-map-rt", "maprt@example.com", "Owner");
        var taskId = await SeedTaskAsync(user, "Task");

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var labelId = LabelId.New();
        db.Labels.Add(Label.Create(labelId, user, "Tag", "red", DateTime.UtcNow));
        db.TaskLabels.Add(new TaskLabel(TaskId.From(taskId), labelId));
        await db.SaveChangesAsync();

        var row = await db.TaskLabels.SingleOrDefaultAsync(tl => tl.TaskId == TaskId.From(taskId) && tl.LabelId == labelId);
        row.Should().NotBeNull("the composite value-converted key persists and reads back");
    }

    [Fact]
    public async Task ListLabelIdsForTasksAsync_translates_and_is_caller_scoped()
    {
        var user = await CreateUserAsync("g-map-join", "mapjoin@example.com", "Owner");
        var other = await CreateUserAsync("g-map-other", "mapother@example.com", "Other");
        var taskId = await SeedTaskAsync(user, "Task");

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mine = LabelId.New();
        var theirs = LabelId.New();
        db.Labels.Add(Label.Create(mine, user, "Mine", null, DateTime.UtcNow));
        db.Labels.Add(Label.Create(theirs, other, "Theirs", null, DateTime.UtcNow));
        db.TaskLabels.Add(new TaskLabel(TaskId.From(taskId), mine));
        db.TaskLabels.Add(new TaskLabel(TaskId.From(taskId), theirs));
        await db.SaveChangesAsync();

        var repo = scope.ServiceProvider.GetRequiredService<ITaskLabelRepository>();
        var result = await repo.ListLabelIdsForTasksAsync([TaskId.From(taskId)], user, CancellationToken.None);

        result.Should().ContainKey(TaskId.From(taskId));
        result[TaskId.From(taskId)].Should().BeEquivalentTo([mine.Value], "the join is caller-scoped — only the caller's label, never the other user's");
    }
}
