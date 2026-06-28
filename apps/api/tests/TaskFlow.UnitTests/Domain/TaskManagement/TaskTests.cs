using FluentAssertions;
using TaskFlow.Domain.IdentityAccess;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;
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

    [Fact]
    public void Create_with_a_date_time_due_sets_due_date_and_due_has_time_true_without_bumping_version()
    {
        var due = new DateTime(2026, 6, 21, 15, 0, 0, DateTimeKind.Utc);

        var task = Task.Create(TaskId.From(Guid.NewGuid()), UserId.New(), "Kupic mleko", "a0", CreatedInstant, due, dueHasTime: true);

        task.DueDate.Should().Be(due);
        task.DueHasTime.Should().BeTrue();
        task.Version.Should().Be(0, "creation is not a mutation and never bumps version");
    }

    [Fact]
    public void Create_with_a_date_only_due_sets_due_date_and_due_has_time_false_without_bumping_version()
    {
        var due = new DateTime(2026, 6, 23, 22, 0, 0, DateTimeKind.Utc);

        var task = Task.Create(TaskId.From(Guid.NewGuid()), UserId.New(), "Raport jutro", "a0", CreatedInstant, due, dueHasTime: false);

        task.DueDate.Should().Be(due);
        task.DueHasTime.Should().BeFalse();
        task.Version.Should().Be(0, "creation is not a mutation and never bumps version");
    }

    [Fact]
    public void Create_without_a_due_leaves_both_due_date_and_due_has_time_null_and_version_zero()
    {
        var task = Task.Create(TaskId.From(Guid.NewGuid()), UserId.New(), "Write the spec", "a0", CreatedInstant);

        task.DueDate.Should().BeNull();
        task.DueHasTime.Should().BeNull();
        task.Version.Should().Be(0, "creation is not a mutation and never bumps version");
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
        task.MoveToProject(ProjectId.From(Guid.NewGuid()), MutateInstant);
        task.SoftDelete(MutateInstant);

        task.CreatedBy.Should().Be(createdBy);
    }

    [Fact]
    public void MoveToProject_sets_project_id_and_bumps_version_and_updated_at()
    {
        // slice 004 US2 (R7): assigning a project removes the task from the Inbox (FR-021).
        var task = NewTask();
        var projectId = ProjectId.From(Guid.NewGuid());

        task.MoveToProject(projectId, MutateInstant);

        task.ProjectId.Should().Be(projectId);
        task.Version.Should().Be(1);
        task.UpdatedAt.Should().Be(MutateInstant);
    }

    [Fact]
    public void MoveToProject_with_null_clears_project_id_to_the_inbox_and_bumps_version()
    {
        // R7: a null target moves the task BACK to the Inbox (the natural inverse of FR-021).
        var task = NewTask();
        task.MoveToProject(ProjectId.From(Guid.NewGuid()), MutateInstant);

        task.MoveToProject(null, LaterInstant);

        task.ProjectId.Should().BeNull("a null target clears the project, returning the task to the Inbox");
        task.Version.Should().Be(2, "every move is a mutation");
        task.UpdatedAt.Should().Be(LaterInstant);
    }

    // ── slice 005: priority (R2) ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("P0")]
    [InlineData("P1")]
    [InlineData("P2")]
    [InlineData("P3")]
    public void SetPriority_sets_a_closed_set_token_and_bumps_version(string priority)
    {
        var task = NewTask();

        task.SetPriority(priority, MutateInstant);

        task.Priority.Should().Be(priority);
        task.Version.Should().Be(1);
        task.UpdatedAt.Should().Be(MutateInstant);
    }

    [Fact]
    public void SetPriority_with_null_clears_the_priority_and_still_bumps_version()
    {
        var task = NewTask();
        task.SetPriority("P0", MutateInstant);

        task.SetPriority(null, LaterInstant);

        task.Priority.Should().BeNull("null = unprioritized");
        task.Version.Should().Be(2, "a no-op-equal set still bumps version, consistent with the other setters");
        task.UpdatedAt.Should().Be(LaterInstant);
    }

    [Theory]
    [InlineData("p0")]
    [InlineData("P4")]
    [InlineData("high")]
    [InlineData("")]
    public void SetPriority_rejects_an_out_of_set_token(string priority)
    {
        var task = NewTask();

        var act = () => task.SetPriority(priority, MutateInstant);

        act.Should().Throw<ArgumentException>("the closed-set guard is belt-and-braces behind the command validator");
        task.Version.Should().Be(0, "a rejected set leaves the row untouched");
    }

    // ── slice 005: reschedule (R4) ─────────────────────────────────────────────────────────

    [Fact]
    public void Reschedule_sets_the_due_pair_and_bumps_version()
    {
        var task = NewTask();
        var due = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);

        task.Reschedule(due, dueHasTime: true, MutateInstant);

        task.DueDate.Should().Be(due);
        task.DueHasTime.Should().BeTrue();
        task.Version.Should().Be(1);
        task.UpdatedAt.Should().Be(MutateInstant);
    }

    [Fact]
    public void Reschedule_with_both_null_clears_the_due_date()
    {
        var task = NewTask();
        task.Reschedule(new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc), true, MutateInstant);

        task.Reschedule(null, null, LaterInstant);

        task.DueDate.Should().BeNull();
        task.DueHasTime.Should().BeNull();
        task.Version.Should().Be(2);
    }

    // ── slice 005: the whole-object editor replace (R4) ────────────────────────────────────

    [Fact]
    public void EditTask_replaces_all_editable_fields_in_one_touch()
    {
        var task = NewTask();
        var projectId = ProjectId.From(Guid.NewGuid());
        var due = new DateTime(2026, 8, 2, 13, 0, 0, DateTimeKind.Utc);

        task.EditTask("Edited title", "A description", "P1", due, dueHasTime: true, projectId, MutateInstant);

        task.Title.Should().Be("Edited title");
        task.Description.Should().Be("A description");
        task.Priority.Should().Be("P1");
        task.DueDate.Should().Be(due);
        task.DueHasTime.Should().BeTrue();
        task.ProjectId.Should().Be(projectId);
        task.Version.Should().Be(1, "the whole-object replace is a single mutation (one Touch)");
        task.UpdatedAt.Should().Be(MutateInstant);
    }

    [Fact]
    public void EditTask_clears_nullable_fields_when_passed_null()
    {
        var task = NewTask();
        task.EditTask("First", "desc", "P0", new DateTime(2026, 8, 2, 13, 0, 0, DateTimeKind.Utc), true, ProjectId.From(Guid.NewGuid()), MutateInstant);

        task.EditTask("Second", null, null, null, null, null, LaterInstant);

        task.Title.Should().Be("Second");
        task.Description.Should().BeNull();
        task.Priority.Should().BeNull();
        task.DueDate.Should().BeNull();
        task.DueHasTime.Should().BeNull();
        task.ProjectId.Should().BeNull("a null projectId returns the task to the Inbox");
        task.Version.Should().Be(2);
    }

    [Fact]
    public void EditTask_trims_the_title_and_an_over_long_title_throws()
    {
        var task = NewTask();

        task.EditTask("  spaced  ", null, null, null, null, null, MutateInstant);
        task.Title.Should().Be("spaced");

        var act = () => task.EditTask(new string('x', 501), null, null, null, null, null, LaterInstant);
        act.Should().Throw<ArgumentException>("the domain NormalizeTitle guard backs the command validator");
    }

    [Fact]
    public void EditTask_rejects_an_out_of_set_priority()
    {
        var task = NewTask();

        var act = () => task.EditTask("Title", null, "P9", null, null, null, MutateInstant);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EditTask_treats_a_whitespace_only_description_as_null()
    {
        var task = NewTask();

        task.EditTask("Title", "   ", null, null, null, null, MutateInstant);

        task.Description.Should().BeNull("a whitespace-only description is normalized to no description");
    }
}
