using System.Net;
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
/// contracts/recurrence-api.md §SetTaskStatus spawn (slice 012, T004; frozen clock = 2026-06-10 12:00 Warsaw):
/// on/after the due day the successor is created inside the status write (FR-008, AS-02/03/05), before it the
/// spawn is scheduled (FR-009, AS-04), retries are idempotent, the chain continues, shared-project assignees are
/// re-validated without re-notification, every owner's labels follow, and cancel never spawns (FR-010, AS-06).
/// </summary>
public sealed class CompleteRecurringTaskTests : RecurrenceTestBase
{
    private async Task<(UserId Owner, string Token)> NewOwnerAsync(string slug)
    {
        var owner = await CreateUserAsync($"g-{slug}", $"{slug}@example.com", "Owner");
        return (owner, TokenFor(owner));
    }

    [Fact]
    public async Task Completing_an_instance_due_today_spawns_the_successor_in_the_same_write()
    {
        var (owner, token) = await NewOwnerAsync("spawn-today");
        var taskId = await SeedRecurringTaskAsync(owner, "Podlej kwiaty", DueOnDay(0), new DailyRecurrence(), description: "Rano", priority: "P1");
        var version = (await LoadTaskAsync(taskId))!.Version;

        var response = await SetStatusAsync(token, taskId, "done", version);

        response.Id.Should().Be(taskId, "the response is the completed PREDECESSOR");
        response.Status.Should().Be("done");

        var successor = await FindLiveSuccessorAsync(taskId);
        successor.Should().NotBeNull("a successor row exists (FR-008)");
        successor!.Title.Should().Be("Podlej kwiaty");
        successor.Description.Should().Be("Rano");
        successor.Priority.Should().Be("P1");
        successor.DueDate.Should().Be(DueOnDay(1), "daily → tomorrow (AS-02)");
        successor.DueHasTime.Should().BeFalse();
        successor.Status.Should().Be(TaskStatus.Backlog);
        successor.CompletedAt.Should().BeNull();
        successor.RecurrenceRule.Should().Be(new DailyRecurrence(), "the rule carries forward so the chain continues");
        successor.CycleId.Should().BeNull("FR-008: unassigned to any cycle");
        successor.CarriedOver.Should().BeFalse();
        successor.CreatedBy.Should().Be(owner);
        successor.Version.Should().Be(0);
        string.CompareOrdinal(successor.Position, (await LoadTaskAsync(taskId))!.Position).Should().BePositive("adjacent AFTER the predecessor");
        (await CountScheduledEnvelopesAsync<SpawnRecurrenceSuccessor>(taskId)).Should().Be(0, "nothing is scheduled when the spawn happened now");
    }

    [Fact]
    public async Task Completing_late_computes_the_successor_from_the_ORIGINAL_due_even_if_that_is_in_the_past()
    {
        var (owner, token) = await NewOwnerAsync("spawn-late");
        // Due June 8 (two days ago), every 3 days, completed "today" June 10 → successor due June 11 (anchor + 3), not June 13.
        var taskId = await SeedRecurringTaskAsync(owner, "Co 3 dni", DueOnDay(-2), new EveryNDaysRecurrence(3));
        var version = (await LoadTaskAsync(taskId))!.Version;

        await SetStatusAsync(token, taskId, "done", version);

        var successor = await FindLiveSuccessorAsync(taskId);
        successor!.DueDate.Should().Be(DueOnDay(1), "ASM-09: anchor + interval, regardless of the completion date");
    }

    [Fact]
    public async Task Completing_very_late_never_skips_occurrences_the_successor_may_already_be_overdue()
    {
        var (owner, token) = await NewOwnerAsync("spawn-overdue");
        // Due June 1, every 3 days, completed June 10 → successor due June 4 (already past) — Clarifications Q2.
        var taskId = await SeedRecurringTaskAsync(owner, "Zaległe", DueOnDay(-9), new EveryNDaysRecurrence(3));
        var version = (await LoadTaskAsync(taskId))!.Version;

        await SetStatusAsync(token, taskId, "done", version);

        (await FindLiveSuccessorAsync(taskId))!.DueDate.Should().Be(DueOnDay(-6));
    }

    [Fact]
    public async Task Weekday_rule_lands_on_the_next_listed_day_AS03()
    {
        var (owner, token) = await NewOwnerAsync("spawn-weekdays");
        // 2026-06-08 is a Monday; rule Mon+Wed; completing it (late, on Wed June 10) → next = Wed June 10 (strictly after Mon).
        var taskId = await SeedRecurringTaskAsync(owner, "Pn i Śr", DueOnDay(-2), new WeekdaysRecurrence([1, 3]));
        var version = (await LoadTaskAsync(taskId))!.Version;

        await SetStatusAsync(token, taskId, "done", version);

        (await FindLiveSuccessorAsync(taskId))!.DueDate.Should().Be(DueOnDay(0), "Monday → Wednesday");
    }

    [Fact]
    public async Task Completing_before_the_due_day_spawns_nothing_and_schedules_exactly_one_deferred_message()
    {
        var (owner, token) = await NewOwnerAsync("early");
        var taskId = await SeedRecurringTaskAsync(owner, "Jutro", DueOnDay(1), new DailyRecurrence());
        var version = (await LoadTaskAsync(taskId))!.Version;

        await SetStatusAsync(token, taskId, "done", version);

        (await FindLiveSuccessorAsync(taskId)).Should().BeNull("AS-04: no premature spawn");
        (await CountScheduledEnvelopesAsync<SpawnRecurrenceSuccessor>(taskId)).Should().Be(1, "exactly one SpawnRecurrenceSuccessor is parked for the due day");
    }

    [Fact]
    public async Task A_same_status_retry_neither_spawns_twice_nor_schedules_twice()
    {
        var (owner, token) = await NewOwnerAsync("retry");
        var todayTask = await SeedRecurringTaskAsync(owner, "Dziś", DueOnDay(0), new DailyRecurrence());
        var earlyTask = await SeedRecurringTaskAsync(owner, "Jutro", DueOnDay(1), new DailyRecurrence(), position: "a1");

        var done = await SetStatusAsync(token, todayTask, "done", (await LoadTaskAsync(todayTask))!.Version);
        await SetStatusAsync(token, todayTask, "done", done.Version);
        var early = await SetStatusAsync(token, earlyTask, "done", (await LoadTaskAsync(earlyTask))!.Version);
        await SetStatusAsync(token, earlyTask, "done", early.Version);

        (await ListSuccessorsIncludingDeletedAsync(todayTask)).Should().HaveCount(1, "idempotent under optimistic retry");
        (await CountScheduledEnvelopesAsync<SpawnRecurrenceSuccessor>(earlyTask)).Should().Be(1);
    }

    [Fact]
    public async Task The_chain_continues_completing_the_successor_on_its_due_day_spawns_a_third_instance()
    {
        var (owner, token) = await NewOwnerAsync("chain");
        var first = await SeedRecurringTaskAsync(owner, "Łańcuch", DueOnDay(-1), new DailyRecurrence());

        await SetStatusAsync(token, first, "done", (await LoadTaskAsync(first))!.Version);
        var second = (await FindLiveSuccessorAsync(first))!;
        second.DueDate.Should().Be(DueOnDay(0), "yesterday + 1 = today");

        await SetStatusAsync(token, second.Id.Value, "done", second.Version);

        var third = await FindLiveSuccessorAsync(second.Id.Value);
        third.Should().NotBeNull("a second successor is generable from the first (FR-008)");
        third!.DueDate.Should().Be(DueOnDay(1));
        third.RecurrenceRule.Should().Be(new DailyRecurrence());
    }

    [Fact]
    public async Task Shared_project_successor_keeps_current_members_drops_a_former_member_and_raises_no_TaskAssigned()
    {
        var owner = await CreateUserAsync("g-shared-spawn-o", "shared-spawn-o@example.com", "Owner");
        var editor = await CreateUserAsync("g-shared-spawn-e", "shared-spawn-e@example.com", "Editor");
        var leaver = await CreateUserAsync("g-shared-spawn-l", "shared-spawn-l@example.com", "Leaver");
        var ownerToken = TokenFor(owner);
        var project = await ShareProjectAsync(ownerToken, await CreateProjectAsync(ownerToken));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        await SeedMembershipAsync(project.Id, leaver, MembershipRoles.Editor);
        var taskId = await SeedRecurringTaskAsync(owner, "Wspólne", DueOnDay(0), new DailyRecurrence(), projectId: project.Id);
        var assigned = await SetAssigneesAsync(ownerToken, taskId, (await LoadTaskAsync(taskId))!.Version, editor.Value, leaver.Value);
        await DeleteMembershipRowAsync(project.Id, leaver);
        var envelopesBefore = await CountScheduledEnvelopesAsync<TaskAssigned>(taskId);

        await SetStatusAsync(ownerToken, taskId, "done", assigned.Version);

        var successor = (await FindLiveSuccessorAsync(taskId))!;
        successor.ProjectId!.Value.Value.Should().Be(project.Id);
        successor.Assignees.Select(a => a.UserId).Should().BeEquivalentTo([editor], "current members are carried, the former member is dropped (FR-066)");
        (await CountScheduledEnvelopesAsync<TaskAssigned>(taskId)).Should().Be(envelopesBefore, "carry-forward does not re-notify");
    }

    [Fact]
    public async Task Every_owners_label_rows_follow_the_successor()
    {
        var owner = await CreateUserAsync("g-labels-o", "labels-o@example.com", "Owner");
        var editor = await CreateUserAsync("g-labels-e", "labels-e@example.com", "Editor");
        var ownerToken = TokenFor(owner);
        var project = await ShareProjectAsync(ownerToken, await CreateProjectAsync(ownerToken));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        var taskId = await SeedRecurringTaskAsync(owner, "Z etykietami", DueOnDay(0), new DailyRecurrence(), projectId: project.Id);
        var ownerLabel = await SeedLabelOnTaskAsync(owner, taskId, "Dom");
        var editorLabel = await SeedLabelOnTaskAsync(editor, taskId, "Praca");

        await SetStatusAsync(ownerToken, taskId, "done", (await LoadTaskAsync(taskId))!.Version);

        var successor = (await FindLiveSuccessorAsync(taskId))!;
        (await ListAllLabelIdsAsync(successor.Id.Value)).Should().BeEquivalentTo([ownerLabel, editorLabel]);
    }

    [Fact]
    public async Task Cancelling_a_recurring_instance_spawns_nothing_FR010()
    {
        var (owner, token) = await NewOwnerAsync("cancel");
        var taskId = await SeedRecurringTaskAsync(owner, "Anulowane", DueOnDay(0), new DailyRecurrence());

        await SetStatusAsync(token, taskId, "cancelled", (await LoadTaskAsync(taskId))!.Version);

        (await FindLiveSuccessorAsync(taskId)).Should().BeNull("AS-06");
        (await CountScheduledEnvelopesAsync<SpawnRecurrenceSuccessor>(taskId)).Should().Be(0);
    }

    [Fact]
    public async Task A_non_recurring_task_or_a_recurring_task_without_due_never_spawns()
    {
        var (owner, token) = await NewOwnerAsync("plain");
        var plain = await SeedRecurringTaskAsync(owner, "Zwykłe", DueOnDay(0), null);

        await SetStatusAsync(token, plain, "done", (await LoadTaskAsync(plain))!.Version);

        (await FindLiveSuccessorAsync(plain)).Should().BeNull();
    }

    [Fact]
    public async Task Status_write_authorization_is_unchanged_a_viewer_cannot_complete_a_shared_recurring_task()
    {
        var owner = await CreateUserAsync("g-authz-o", "authz-o@example.com", "Owner");
        var viewer = await CreateUserAsync("g-authz-v", "authz-v@example.com", "Viewer");
        var ownerToken = TokenFor(owner);
        var project = await ShareProjectAsync(ownerToken, await CreateProjectAsync(ownerToken));
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);
        var taskId = await SeedRecurringTaskAsync(owner, "Wspólne", DueOnDay(0), new DailyRecurrence(), projectId: project.Id);

        using var response = await SendStatusAsync(TokenFor(viewer), taskId, "done", (await LoadTaskAsync(taskId))!.Version);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await FindLiveSuccessorAsync(taskId)).Should().BeNull();
    }
}
