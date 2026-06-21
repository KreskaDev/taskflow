using FluentAssertions;
using TaskFlow.Domain.IdentityAccess;
using Task = TaskFlow.Domain.TaskManagement.Task;
using TaskStatus = TaskFlow.Domain.TaskManagement.TaskStatus;

namespace TaskFlow.UnitTests.Domain.TaskManagement;

/// <summary>
/// Aggregate invariants for <see cref="Task"/> (ENT-01, T008): title is trimmed-non-empty and
/// ≤ 500; status defaults to <c>Backlog</c>; the done ↔ backlog toggle stamps/clears
/// <c>CompletedAt</c>; <c>CreatedBy</c> is immutable; every mutator bumps <c>Version</c> and
/// stamps <c>UpdatedAt</c>; soft-delete is idempotent (data-model.md ENT-01).
/// </summary>
public sealed class TaskTests
{
    private static readonly DateTime CreatedInstant = new(2026, 1, 2, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime MutateInstant = new(2026, 6, 20, 9, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime LaterInstant = new(2026, 6, 21, 10, 0, 0, DateTimeKind.Utc);

    private static Task NewTask(string title = "Write the spec", string position = "a0")
        => Task.Create(TaskId.From(Guid.NewGuid()), UserId.New(), title, position, CreatedInstant);

    [Fact]
    public void Create_populates_fields_defaults_to_backlog_and_stamps_both_timestamps()
    {
        var id = TaskId.From(Guid.NewGuid());
        var createdBy = UserId.New();

        var task = Task.Create(id, createdBy, "Write the spec", "a0", CreatedInstant);

        task.Id.Should().Be(id);
        task.CreatedBy.Should().Be(createdBy);
        task.Title.Should().Be("Write the spec");
        task.Position.Should().Be("a0");
        task.Status.Should().Be(TaskStatus.Backlog);
        task.Version.Should().Be(0);
        task.CreatedAt.Should().Be(CreatedInstant);
        task.UpdatedAt.Should().Be(CreatedInstant);
        task.CompletedAt.Should().BeNull();
        task.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Create_trims_the_title()
    {
        var task = Task.Create(TaskId.From(Guid.NewGuid()), UserId.New(), "  Write the spec  ", "a0", CreatedInstant);

        task.Title.Should().Be("Write the spec");
    }

    [Fact]
    public void Create_accepts_a_title_of_exactly_500_characters()
    {
        var title = new string('x', 500);

        var task = Task.Create(TaskId.From(Guid.NewGuid()), UserId.New(), title, "a0", CreatedInstant);

        task.Title.Should().Be(title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_title(string blank)
    {
        var act = () => Task.Create(TaskId.From(Guid.NewGuid()), UserId.New(), blank, "a0", CreatedInstant);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_a_title_longer_than_500_characters_after_trim()
    {
        var tooLong = new string('x', 501);

        var act = () => Task.Create(TaskId.From(Guid.NewGuid()), UserId.New(), tooLong, "a0", CreatedInstant);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rename_replaces_the_trimmed_title_and_bumps_version_and_updated_at()
    {
        var task = NewTask();

        task.Rename("  New title  ", MutateInstant);

        task.Title.Should().Be("New title");
        task.Version.Should().Be(1);
        task.UpdatedAt.Should().Be(MutateInstant);
        task.Status.Should().Be(TaskStatus.Backlog, "renaming does not change status");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_rejects_a_blank_title(string blank)
    {
        var task = NewTask();

        var act = () => task.Rename(blank, MutateInstant);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rename_rejects_a_title_longer_than_500_characters_after_trim()
    {
        var task = NewTask();
        var tooLong = new string('x', 501);

        var act = () => task.Rename(tooLong, MutateInstant);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkDone_sets_status_done_and_stamps_completed_at_and_bumps_version_and_updated_at()
    {
        var task = NewTask();

        task.MarkDone(MutateInstant);

        task.Status.Should().Be(TaskStatus.Done);
        task.CompletedAt.Should().Be(MutateInstant);
        task.Version.Should().Be(1);
        task.UpdatedAt.Should().Be(MutateInstant);
    }

    [Fact]
    public void MarkBacklog_sets_status_backlog_and_clears_completed_at_and_bumps_version_and_updated_at()
    {
        var task = NewTask();
        task.MarkDone(MutateInstant);

        task.MarkBacklog(LaterInstant);

        task.Status.Should().Be(TaskStatus.Backlog);
        task.CompletedAt.Should().BeNull();
        task.Version.Should().Be(2);
        task.UpdatedAt.Should().Be(LaterInstant);
    }

    [Fact]
    public void Reorder_replaces_the_position_and_bumps_version_and_updated_at()
    {
        var task = NewTask(position: "a0");

        task.Reorder("a5", MutateInstant);

        task.Position.Should().Be("a5");
        task.Version.Should().Be(1);
        task.UpdatedAt.Should().Be(MutateInstant);
    }

    [Fact]
    public void SoftDelete_stamps_deleted_at_and_bumps_version_and_updated_at()
    {
        var task = NewTask();

        task.SoftDelete(MutateInstant);

        task.DeletedAt.Should().Be(MutateInstant);
        task.Version.Should().Be(1);
        task.UpdatedAt.Should().Be(MutateInstant);
    }

    [Fact]
    public void SoftDelete_is_idempotent_a_second_call_changes_nothing()
    {
        var task = NewTask();
        task.SoftDelete(MutateInstant);

        task.SoftDelete(LaterInstant);

        task.DeletedAt.Should().Be(MutateInstant, "deleted_at is set once and never re-stamped");
        task.Version.Should().Be(1, "the second soft-delete is a guarded no-op");
        task.UpdatedAt.Should().Be(MutateInstant, "the no-op does not re-stamp updated_at");
    }

    [Fact]
    public void CreatedBy_is_immutable_across_every_mutator()
    {
        var createdBy = UserId.New();
        var task = Task.Create(TaskId.From(Guid.NewGuid()), createdBy, "Write the spec", "a0", CreatedInstant);

        task.Rename("New title", MutateInstant);
        task.MarkDone(MutateInstant);
        task.MarkBacklog(MutateInstant);
        task.Reorder("a5", MutateInstant);
        task.SoftDelete(MutateInstant);

        task.CreatedBy.Should().Be(createdBy);
    }
}
