using FluentAssertions;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;
using Task = TaskFlow.Domain.TaskManagement.Task;
using TaskStatus = TaskFlow.Domain.TaskManagement.TaskStatus;

namespace TaskFlow.UnitTests.Domain.TaskManagement;

/// <summary>
/// Slice 012 aggregate facts (T007): the "rule requires a due date" invariant on every write path (D9),
/// <c>SetRecurrence</c> idempotency, the <c>SpawnSuccessor</c> field matrix (data-model.md) and the
/// <c>DetachSuccessorLink</c> undo seam (D6).
/// </summary>
public sealed class TaskRecurrenceTests
{
    private static readonly DateTime CreatedInstant = new(2026, 1, 2, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime MutateInstant = new(2026, 6, 20, 9, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime LaterInstant = new(2026, 6, 21, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>2026-06-10 00:00 Europe/Warsaw stored as the date-only UTC instant.</summary>
    private static readonly DateTime DueInstant = new(2026, 6, 9, 22, 0, 0, DateTimeKind.Utc);

    private static Task NewPlainTask()
        => Task.Create(TaskId.From(Guid.NewGuid()), UserId.New(), "Podlej kwiaty", "a0", CreatedInstant);

    private static Task NewRecurringTask(RecurrenceRule? rule = null)
    {
        var task = Task.Create(TaskId.From(Guid.NewGuid()), UserId.New(), "Podlej kwiaty", "a0", CreatedInstant, DueInstant, false);
        task.SetRecurrence(rule ?? new DailyRecurrence(), MutateInstant);
        return task;
    }

    [Fact]
    public void SetRecurrence_on_a_task_without_a_due_date_is_refused()
    {
        var task = NewPlainTask();

        var act = () => task.SetRecurrence(new DailyRecurrence(), MutateInstant);

        act.Should().Throw<RecurrenceRequiresDueDateException>();
        task.RecurrenceRule.Should().BeNull();
    }

    [Fact]
    public void SetRecurrence_stores_the_rule_bumps_version_and_is_idempotent_for_the_same_rule()
    {
        var task = Task.Create(TaskId.From(Guid.NewGuid()), UserId.New(), "Podlej kwiaty", "a0", CreatedInstant, DueInstant, false);

        task.SetRecurrence(new EveryNDaysRecurrence(3), MutateInstant);
        var versionAfterFirst = task.Version;
        task.SetRecurrence(new EveryNDaysRecurrence(3), LaterInstant);

        task.RecurrenceRule.Should().Be(new EveryNDaysRecurrence(3));
        versionAfterFirst.Should().Be(1);
        task.Version.Should().Be(versionAfterFirst, "the same rule is a no-op");
        task.UpdatedAt.Should().Be(MutateInstant);
    }

    [Fact]
    public void SetRecurrence_null_clears_the_rule()
    {
        var task = NewRecurringTask();

        task.SetRecurrence(null, LaterInstant);

        task.RecurrenceRule.Should().BeNull();
    }

    [Fact]
    public void Reschedule_to_no_due_date_is_refused_while_a_rule_exists()
    {
        var task = NewRecurringTask();

        var act = () => task.Reschedule(null, null, LaterInstant);

        act.Should().Throw<RecurrenceRequiresDueDateException>();
        task.DueDate.Should().Be(DueInstant, "nothing changed");
    }

    [Fact]
    public void Reschedule_to_another_date_moves_the_anchor_of_a_recurring_task()
    {
        var task = NewRecurringTask();
        var newDue = DueInstant.AddDays(5);

        task.Reschedule(newDue, false, LaterInstant);

        task.DueDate.Should().Be(newDue);
        task.RecurrenceRule.Should().Be(new DailyRecurrence());
    }

    [Fact]
    public void EditTask_with_a_rule_but_no_due_date_is_refused_and_leaves_the_task_untouched()
    {
        var task = NewRecurringTask();

        var act = () => task.EditTask("Edited", null, null, null, null, null, new DailyRecurrence(), LaterInstant);

        act.Should().Throw<RecurrenceRequiresDueDateException>();
        task.Title.Should().Be("Podlej kwiaty");
    }

    [Fact]
    public void EditTask_replaces_the_rule_as_part_of_the_whole_object_save()
    {
        var task = NewRecurringTask();

        task.EditTask("Podlej kwiaty", null, null, DueInstant, false, null, new WeekdaysRecurrence([1, 3]), LaterInstant);

        task.RecurrenceRule.Should().Be(new WeekdaysRecurrence([1, 3]));

        task.EditTask("Podlej kwiaty", null, null, null, null, null, null, LaterInstant);

        task.RecurrenceRule.Should().BeNull("clearing due AND rule together is legal");
        task.DueDate.Should().BeNull();
    }

    [Fact]
    public void SpawnSuccessor_copies_the_carry_forward_fields_and_resets_the_rest()
    {
        var owner = UserId.New();
        var projectId = ProjectId.From(Guid.NewGuid());
        var predecessor = Task.Create(TaskId.From(Guid.NewGuid()), owner, "Podlej kwiaty", "a0", CreatedInstant, DueInstant, false);
        predecessor.EditTask("Podlej kwiaty", "Co 3 dni", "P1", DueInstant, false, projectId, new EveryNDaysRecurrence(3), MutateInstant);
        predecessor.SetCycle(Guid.NewGuid(), MutateInstant);
        predecessor.MarkCarriedOver(MutateInstant);
        predecessor.SetStatus(TaskStatus.Done, MutateInstant);
        var successorId = TaskId.From(Guid.NewGuid());

        var successor = Task.SpawnSuccessor(predecessor, successorId, "a0V", LaterInstant);

        successor.Id.Should().Be(successorId);
        successor.CreatedBy.Should().Be(owner, "ownership follows the predecessor (FR-065)");
        successor.Title.Should().Be("Podlej kwiaty");
        successor.Description.Should().Be("Co 3 dni");
        successor.Priority.Should().Be("P1");
        successor.ProjectId.Should().Be(projectId);
        successor.RecurrenceRule.Should().Be(new EveryNDaysRecurrence(3), "the rule + anchor carry forward");
        successor.DueDate.Should().Be(DueInstant.AddDays(3), "every 3 days from the ORIGINAL due (ASM-09)");
        successor.DueHasTime.Should().BeFalse();
        successor.Status.Should().Be(TaskStatus.Backlog);
        successor.CompletedAt.Should().BeNull();
        successor.Version.Should().Be(0);
        successor.CreatedAt.Should().Be(LaterInstant);
        successor.UpdatedAt.Should().Be(LaterInstant);
        successor.Position.Should().Be("a0V");
        successor.CycleId.Should().BeNull("FR-008: unassigned to any cycle");
        successor.CarriedOver.Should().BeFalse();
        successor.SpawnedFromId.Should().Be(predecessor.Id);
        successor.Assignees.Should().BeEmpty("assignees are applied by the Application helper after membership re-validation");
        successor.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void SpawnSuccessor_keeps_the_Warsaw_wall_clock_of_a_timed_due()
    {
        var timedDue = new DateTime(2026, 3, 28, 8, 0, 0, DateTimeKind.Utc); // 09:00 CET
        var predecessor = Task.Create(TaskId.From(Guid.NewGuid()), UserId.New(), "Stand-up", "a0", CreatedInstant, timedDue, true);
        predecessor.SetRecurrence(new DailyRecurrence(), MutateInstant);

        var successor = Task.SpawnSuccessor(predecessor, TaskId.From(Guid.NewGuid()), "a1", LaterInstant);

        successor.DueDate.Should().Be(new DateTime(2026, 3, 29, 7, 0, 0, DateTimeKind.Utc), "09:00 CEST after the DST switch");
        successor.DueHasTime.Should().BeTrue();
    }

    [Fact]
    public void SpawnSuccessor_requires_a_rule_and_a_due_date()
    {
        var plain = NewPlainTask();

        var act = () => Task.SpawnSuccessor(plain, TaskId.From(Guid.NewGuid()), "a1", LaterInstant);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DetachSuccessorLink_clears_the_link_once_and_bumps_version()
    {
        var predecessor = NewRecurringTask();
        var successor = Task.SpawnSuccessor(predecessor, TaskId.From(Guid.NewGuid()), "a1", LaterInstant);

        successor.DetachSuccessorLink(LaterInstant);
        var version = successor.Version;
        successor.DetachSuccessorLink(LaterInstant);

        successor.SpawnedFromId.Should().BeNull();
        version.Should().Be(1);
        successor.Version.Should().Be(1, "idempotent");
    }
}
