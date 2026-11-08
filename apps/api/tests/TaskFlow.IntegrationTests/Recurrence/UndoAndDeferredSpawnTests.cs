using FluentAssertions;
using TaskFlow.Domain.IdentityAccess;
using DailyRecurrence = TaskFlow.Domain.TaskManagement.DailyRecurrence;
using EveryNDaysRecurrence = TaskFlow.Domain.TaskManagement.EveryNDaysRecurrence;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;
using RecurrenceRule = TaskFlow.Domain.TaskManagement.RecurrenceRule;
using WeekdaysRecurrence = TaskFlow.Domain.TaskManagement.WeekdaysRecurrence;
using TaskFlow.Domain.TaskManagement.Events;
using TaskFlow.IntegrationTests.Infrastructure;
using TaskStatus = TaskFlow.Domain.TaskManagement.TaskStatus;

namespace TaskFlow.IntegrationTests.Recurrence;

/// <summary>
/// contracts/recurrence-api.md §undo + §scheduled message (slice 012, T005): reopening soft-deletes the live
/// successor (or detaches a completed one) and frees the slot (Clarifications Q1, D6); the deferred
/// <see cref="SpawnRecurrenceSuccessor"/> job spawns once and no-ops on every stale condition (D7).
/// </summary>
public sealed class UndoAndDeferredSpawnTests : RecurrenceTestBase
{
    private async Task<(UserId Owner, string Token)> NewOwnerAsync(string slug)
    {
        var owner = await CreateUserAsync($"g-{slug}", $"{slug}@example.com", "Owner");
        return (owner, TokenFor(owner));
    }

    // --- undo = reopen (D6) -----------------------------------------------------------------------------

    [Fact]
    public async Task Reopening_soft_deletes_the_live_successor_schedules_its_reaper_and_frees_the_slot()
    {
        var (owner, token) = await NewOwnerAsync("undo-soft");
        var taskId = await SeedRecurringTaskAsync(owner, "Cofnij", DueOnDay(0), new DailyRecurrence());

        var done = await SetStatusAsync(token, taskId, "done", (await LoadTaskAsync(taskId))!.Version);
        var successor = (await FindLiveSuccessorAsync(taskId))!;

        var reopened = await SetStatusAsync(token, taskId, "backlog", done.Version);

        var successorRow = (await LoadTaskAsync(successor.Id.Value))!;
        successorRow.DeletedAt.Should().NotBeNull("the successor is tombstoned (restorable through the reaper window)");
        (await CountScheduledEnvelopesAsync<ReapDeletedTask>(successor.Id.Value)).Should().Be(1, "the 30s reaper is scheduled like any soft-delete");
        (await FindLiveSuccessorAsync(taskId)).Should().BeNull("the slot is free again");

        var redone = await SetStatusAsync(token, taskId, "done", reopened.Version);
        redone.Status.Should().Be("done");
        var fresh = await FindLiveSuccessorAsync(taskId);
        fresh.Should().NotBeNull("re-completing spawns a FRESH successor (the partial unique index ignores the tombstone)");
        fresh!.Id.Should().NotBe(successor.Id);
        (await ListSuccessorsIncludingDeletedAsync(taskId)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Reopening_when_the_successor_is_already_done_keeps_it_and_only_detaches_the_link()
    {
        var (owner, token) = await NewOwnerAsync("undo-detach");
        var taskId = await SeedRecurringTaskAsync(owner, "Odłącz", DueOnDay(-1), new DailyRecurrence());

        var done = await SetStatusAsync(token, taskId, "done", (await LoadTaskAsync(taskId))!.Version);
        var successor = (await FindLiveSuccessorAsync(taskId))!;
        successor.DueDate.Should().Be(DueOnDay(0));
        await SetStatusAsync(token, successor.Id.Value, "done", successor.Version); // B itself done → spawns C

        var reopened = await SetStatusAsync(token, taskId, "backlog", done.Version);

        var successorRow = (await LoadTaskAsync(successor.Id.Value))!;
        successorRow.DeletedAt.Should().BeNull("a completed successor stays — the chain moved on");
        successorRow.Status.Should().Be(TaskStatus.Done);
        successorRow.SpawnedFromId.Should().BeNull("the link is severed");
        (await FindLiveSuccessorAsync(successor.Id.Value)).Should().NotBeNull("C (B's own successor) is untouched");

        await SetStatusAsync(token, taskId, "done", reopened.Version);
        var fresh = await FindLiveSuccessorAsync(taskId);
        fresh.Should().NotBeNull("A's slot was free, so re-completing spawns anew");
        fresh!.Id.Should().NotBe(successor.Id);
    }

    [Fact]
    public async Task Moving_a_done_instance_to_cancelled_is_leaving_done_so_the_successor_is_soft_deleted_too()
    {
        var (owner, token) = await NewOwnerAsync("undo-cancel");
        var taskId = await SeedRecurringTaskAsync(owner, "Anuluj po", DueOnDay(0), new DailyRecurrence());

        var done = await SetStatusAsync(token, taskId, "done", (await LoadTaskAsync(taskId))!.Version);
        var successor = (await FindLiveSuccessorAsync(taskId))!;

        await SetStatusAsync(token, taskId, "cancelled", done.Version);

        (await LoadTaskAsync(successor.Id.Value))!.DeletedAt.Should().NotBeNull();
        (await FindLiveSuccessorAsync(taskId)).Should().BeNull();
    }

    [Fact]
    public async Task Reopening_a_recurring_task_that_never_spawned_changes_nothing_else()
    {
        var (owner, token) = await NewOwnerAsync("undo-none");
        var taskId = await SeedRecurringTaskAsync(owner, "Bez następnika", DueOnDay(1), new DailyRecurrence());

        var done = await SetStatusAsync(token, taskId, "done", (await LoadTaskAsync(taskId))!.Version);
        var reopened = await SetStatusAsync(token, taskId, "backlog", done.Version);

        reopened.Status.Should().Be("backlog");
        (await ListSuccessorsIncludingDeletedAsync(taskId)).Should().BeEmpty();
    }

    // --- the deferred job (D7) ------------------------------------------------------------------------------

    [Fact]
    public async Task Deferred_job_on_the_due_day_spawns_exactly_once_a_second_delivery_is_a_no_op()
    {
        var (owner, token) = await NewOwnerAsync("job-once");
        var taskId = await SeedRecurringTaskAsync(owner, "Jutro", DueOnDay(1), new DailyRecurrence());
        await SetStatusAsync(token, taskId, "done", (await LoadTaskAsync(taskId))!.Version);
        var completedAt = (await LoadTaskAsync(taskId))!.CompletedAt!.Value;
        (await FindLiveSuccessorAsync(taskId)).Should().BeNull("nothing before the due day");

        Clock.Now = FrozenNow.AddDays(1); // the due day has started
        await SendSpawnAsync(taskId, completedAt);
        await SendSpawnAsync(taskId, completedAt);

        var successors = await ListSuccessorsIncludingDeletedAsync(taskId);
        successors.Should().HaveCount(1, "idempotent — at most one successor per completed instance (FR-009)");
        successors[0].DueDate.Should().Be(DueOnDay(2), "tomorrow + 1 day");
        successors[0].Status.Should().Be(TaskStatus.Backlog);
    }

    [Fact]
    public async Task Deferred_job_no_ops_when_the_instance_was_reopened_before_it_fired()
    {
        var (owner, token) = await NewOwnerAsync("job-reopened");
        var taskId = await SeedRecurringTaskAsync(owner, "Jutro", DueOnDay(1), new DailyRecurrence());
        var done = await SetStatusAsync(token, taskId, "done", (await LoadTaskAsync(taskId))!.Version);
        var completedAt = (await LoadTaskAsync(taskId))!.CompletedAt!.Value;
        await SetStatusAsync(token, taskId, "backlog", done.Version);

        Clock.Now = FrozenNow.AddDays(1);
        await SendSpawnAsync(taskId, completedAt);

        (await ListSuccessorsIncludingDeletedAsync(taskId)).Should().BeEmpty("the instance is no longer done");
    }

    [Fact]
    public async Task Deferred_job_no_ops_when_the_instance_was_cancelled_after_an_early_completion()
    {
        var (owner, token) = await NewOwnerAsync("job-cancelled");
        var taskId = await SeedRecurringTaskAsync(owner, "Jutro", DueOnDay(1), new DailyRecurrence());
        var done = await SetStatusAsync(token, taskId, "done", (await LoadTaskAsync(taskId))!.Version);
        var completedAt = (await LoadTaskAsync(taskId))!.CompletedAt!.Value;
        await SetStatusAsync(token, taskId, "cancelled", done.Version);

        Clock.Now = FrozenNow.AddDays(1);
        await SendSpawnAsync(taskId, completedAt);

        (await ListSuccessorsIncludingDeletedAsync(taskId)).Should().BeEmpty("FR-010: cancelled stops the chain");
    }

    [Fact]
    public async Task Deferred_job_with_a_stale_completed_at_no_ops_and_the_re_completions_own_message_spawns()
    {
        var (owner, token) = await NewOwnerAsync("job-stale");
        var taskId = await SeedRecurringTaskAsync(owner, "Jutro", DueOnDay(1), new DailyRecurrence());
        var done = await SetStatusAsync(token, taskId, "done", (await LoadTaskAsync(taskId))!.Version);
        var staleCompletedAt = (await LoadTaskAsync(taskId))!.CompletedAt!.Value;
        var reopened = await SetStatusAsync(token, taskId, "backlog", done.Version);
        Clock.Now = FrozenNow.AddMinutes(5);
        await SetStatusAsync(token, taskId, "done", reopened.Version);
        var freshCompletedAt = (await LoadTaskAsync(taskId))!.CompletedAt!.Value;
        freshCompletedAt.Should().NotBe(staleCompletedAt);

        Clock.Now = FrozenNow.AddDays(1);
        await SendSpawnAsync(taskId, staleCompletedAt);
        (await ListSuccessorsIncludingDeletedAsync(taskId)).Should().BeEmpty("the stale message belongs to the undone completion");

        await SendSpawnAsync(taskId, freshCompletedAt);
        (await ListSuccessorsIncludingDeletedAsync(taskId)).Should().HaveCount(1, "the re-completion's own message spawns");
    }

    [Fact]
    public async Task Deferred_job_re_schedules_instead_of_spawning_early_when_the_done_instance_was_rescheduled_later()
    {
        var (owner, token) = await NewOwnerAsync("job-moved");
        var taskId = await SeedRecurringTaskAsync(owner, "Jutro", DueOnDay(1), new DailyRecurrence());
        var done = await SetStatusAsync(token, taskId, "done", (await LoadTaskAsync(taskId))!.Version);
        var completedAt = (await LoadTaskAsync(taskId))!.CompletedAt!.Value;
        // The anchor moves two days further while the instance stays done (contract: reschedule moves the anchor).
        using (var moved = await SendAsync(HttpMethod.Patch, DuePath(taskId), token, new { dueDate = DueOnDay(3), dueHasTime = false, version = done.Version }))
        {
            moved.EnsureSuccessStatusCode();
        }

        Clock.Now = FrozenNow.AddDays(1); // the ORIGINAL due day — the parked message fires now
        var before = await CountScheduledEnvelopesAsync<SpawnRecurrenceSuccessor>(taskId);
        await SendSpawnAsync(taskId, completedAt);

        (await ListSuccessorsIncludingDeletedAsync(taskId)).Should().BeEmpty("EC-05: never spawn before the CURRENT due day");
        (await CountScheduledEnvelopesAsync<SpawnRecurrenceSuccessor>(taskId)).Should().Be(before + 1, "the job re-parks itself at the new due day");

        Clock.Now = FrozenNow.AddDays(3); // the NEW due day
        await SendSpawnAsync(taskId, completedAt);
        var successors = await ListSuccessorsIncludingDeletedAsync(taskId);
        successors.Should().HaveCount(1);
        successors[0].DueDate.Should().Be(DueOnDay(4), "anchor (day 3) + daily");
    }

    [Fact]
    public async Task Deferred_job_no_ops_when_the_rule_was_removed_meanwhile()
    {
        var (owner, token) = await NewOwnerAsync("job-norule");
        var taskId = await SeedRecurringTaskAsync(owner, "Jutro", DueOnDay(1), new DailyRecurrence());
        var done = await SetStatusAsync(token, taskId, "done", (await LoadTaskAsync(taskId))!.Version);
        var completedAt = (await LoadTaskAsync(taskId))!.CompletedAt!.Value;
        var body = await GetTaskBodyAsync(token, taskId);
        using (var cleared = await SendAsync(HttpMethod.Patch, EditPath(taskId), token, EditBody(body with { Version = done.Version }, null)))
        {
            cleared.EnsureSuccessStatusCode();
        }

        Clock.Now = FrozenNow.AddDays(1);
        await SendSpawnAsync(taskId, completedAt);

        (await ListSuccessorsIncludingDeletedAsync(taskId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Deferred_job_no_ops_when_the_instance_was_soft_deleted()
    {
        var (owner, token) = await NewOwnerAsync("job-deleted");
        var taskId = await SeedRecurringTaskAsync(owner, "Jutro", DueOnDay(1), new DailyRecurrence());
        await SetStatusAsync(token, taskId, "done", (await LoadTaskAsync(taskId))!.Version);
        var completedAt = (await LoadTaskAsync(taskId))!.CompletedAt!.Value;
        using (var deleted = await SendAsync(HttpMethod.Delete, $"/api/tasks/{taskId}", token))
        {
            deleted.EnsureSuccessStatusCode();
        }

        Clock.Now = FrozenNow.AddDays(1);
        await SendSpawnAsync(taskId, completedAt);

        (await ListSuccessorsIncludingDeletedAsync(taskId)).Should().BeEmpty();
    }
}
