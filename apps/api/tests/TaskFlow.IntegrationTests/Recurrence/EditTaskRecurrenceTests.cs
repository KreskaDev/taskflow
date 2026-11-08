using System.Net;
using FluentAssertions;
using TaskFlow.Domain.IdentityAccess;
using DailyRecurrence = TaskFlow.Domain.TaskManagement.DailyRecurrence;
using EveryNDaysRecurrence = TaskFlow.Domain.TaskManagement.EveryNDaysRecurrence;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;
using RecurrenceRule = TaskFlow.Domain.TaskManagement.RecurrenceRule;
using WeekdaysRecurrence = TaskFlow.Domain.TaskManagement.WeekdaysRecurrence;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests.Recurrence;

/// <summary>
/// contracts/recurrence-api.md §EditTask / §RescheduleDueDate (slice 012, T003): the closed rule union on the
/// whole-object edit, the due-date guard on both write paths (Clarifications Q4, D9), authorization dispatch and
/// the usual 409/401 posture.
/// </summary>
public sealed class EditTaskRecurrenceTests : RecurrenceTestBase
{
    private async Task<(UserId Owner, string Token, Guid TaskId)> SeedOwnedAsync(string slug, bool withDue = true, RecurrenceRule? rule = null)
    {
        var owner = await CreateUserAsync($"g-{slug}", $"{slug}@example.com", "Owner");
        var taskId = await SeedRecurringTaskAsync(owner, "Podlej kwiaty", withDue ? DueOnDay(0) : null, rule);
        return (owner, TokenFor(owner), taskId);
    }

    public static TheoryData<object, string> ValidRules => new()
    {
        { Rule("daily"), "daily" },
        { Rule("every_n_days", interval: 3), "every_n_days" },
        { Rule("weekdays", weekdays: [1, 3]), "weekdays" },
        { Rule("monthly", dayOfMonth: 31), "monthly" },
    };

    [Theory]
    [MemberData(nameof(ValidRules))]
    public async Task Edit_sets_each_rule_type_on_a_due_dated_task_and_echoes_it(object rule, string expectedType)
    {
        var (_, token, taskId) = await SeedOwnedAsync($"rule-{expectedType}");
        var body = await GetTaskBodyAsync(token, taskId);

        using var response = await SendAsync(HttpMethod.Patch, EditPath(taskId), token, EditBody(body, rule));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await response.ReadTaskAsync();
        saved.RecurrenceRule.Should().NotBeNull();
        saved.RecurrenceRule!.Type.Should().Be(expectedType);
        (await LoadTaskAsync(taskId))!.RecurrenceRule.Should().NotBeNull("the rule persisted through the jsonb converter");
    }

    public static TheoryData<object, string> InvalidRules => new()
    {
        { Rule("yearly"), "unknown type" },
        { Rule("every_n_days", interval: 1), "interval below 2" },
        { Rule("every_n_days", interval: 366), "interval above 365" },
        { Rule("every_n_days"), "interval missing" },
        { Rule("weekdays", weekdays: []), "empty weekdays" },
        { Rule("weekdays", weekdays: [1, 1]), "duplicate weekdays" },
        { Rule("weekdays", weekdays: [8]), "weekday above 7" },
        { Rule("monthly", dayOfMonth: 0), "dayOfMonth below 1" },
        { Rule("monthly", dayOfMonth: 32), "dayOfMonth above 31" },
        { Rule("daily", interval: 2), "member not belonging to the type" },
    };

    [Theory]
    [MemberData(nameof(InvalidRules))]
    public async Task Edit_rejects_an_invalid_rule_shape_as_422_validation_failed(object rule, string because)
    {
        var (_, token, taskId) = await SeedOwnedAsync($"bad-{Guid.NewGuid():N}"[..20]);
        var body = await GetTaskBodyAsync(token, taskId);

        using var response = await SendAsync(HttpMethod.Patch, EditPath(taskId), token, EditBody(body, rule));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, because);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task Edit_with_a_rule_on_a_dateless_task_is_422_recurrence_requires_due_date()
    {
        var (_, token, taskId) = await SeedOwnedAsync("dateless", withDue: false);
        var body = await GetTaskBodyAsync(token, taskId);

        using var response = await SendAsync(HttpMethod.Patch, EditPath(taskId), token, EditBody(body, Rule("daily")));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("recurrence_requires_due_date");
        (await LoadTaskAsync(taskId))!.RecurrenceRule.Should().BeNull("nothing was written");
    }

    [Fact]
    public async Task Edit_clearing_the_due_while_keeping_the_rule_is_422_and_the_rule_is_never_dropped_silently()
    {
        var (_, token, taskId) = await SeedOwnedAsync("clear-edit", rule: new DailyRecurrence());
        var body = await GetTaskBodyAsync(token, taskId);

        using var response = await SendAsync(HttpMethod.Patch, EditPath(taskId), token, EditBody(body, Rule("daily"), clearDue: true));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("recurrence_requires_due_date");
        var row = (await LoadTaskAsync(taskId))!;
        row.DueDate.Should().Be(DueOnDay(0));
        row.RecurrenceRule.Should().Be(new DailyRecurrence());
    }

    [Fact]
    public async Task Edit_clearing_due_AND_rule_together_is_allowed()
    {
        var (_, token, taskId) = await SeedOwnedAsync("clear-both", rule: new DailyRecurrence());
        var body = await GetTaskBodyAsync(token, taskId);

        using var response = await SendAsync(HttpMethod.Patch, EditPath(taskId), token, EditBody(body, null, clearDue: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await response.ReadTaskAsync();
        saved.RecurrenceRule.Should().BeNull();
        saved.DueDate.Should().BeNull();
    }

    [Fact]
    public async Task Reschedule_to_no_due_while_a_rule_exists_is_422_recurrence_requires_due_date()
    {
        var (_, token, taskId) = await SeedOwnedAsync("clear-due", rule: new DailyRecurrence());
        var version = (await LoadTaskAsync(taskId))!.Version;

        using var response = await SendAsync(HttpMethod.Patch, DuePath(taskId), token,
            new { dueDate = (DateTime?)null, dueHasTime = (bool?)null, version });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("recurrence_requires_due_date");
    }

    [Fact]
    public async Task Reschedule_to_another_date_moves_the_anchor_and_keeps_the_rule()
    {
        var (_, token, taskId) = await SeedOwnedAsync("move-due", rule: new DailyRecurrence());
        var version = (await LoadTaskAsync(taskId))!.Version;

        using var response = await SendAsync(HttpMethod.Patch, DuePath(taskId), token,
            new { dueDate = DueOnDay(5), dueHasTime = false, version });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await response.ReadTaskAsync();
        saved.DueDate.Should().Be(DueOnDay(5));
        saved.RecurrenceRule!.Type.Should().Be("daily");
    }

    [Fact]
    public async Task Edit_with_null_rule_clears_an_existing_rule()
    {
        var (_, token, taskId) = await SeedOwnedAsync("clear-rule", rule: new EveryNDaysRecurrence(3));
        var body = await GetTaskBodyAsync(token, taskId);

        using var response = await SendAsync(HttpMethod.Patch, EditPath(taskId), token, EditBody(body, null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadTaskAsync()).RecurrenceRule.Should().BeNull();
        (await LoadTaskAsync(taskId))!.RecurrenceRule.Should().BeNull();
    }

    [Fact]
    public async Task Edit_rule_on_another_users_personal_task_is_404()
    {
        var (_, _, taskId) = await SeedOwnedAsync("foreign-owner");
        var stranger = await CreateUserAsync("g-foreign-stranger", "stranger@example.com", "Stranger");
        var body = await GetTaskBodyAsync(TokenFor(stranger), taskId);

        using var response = await SendAsync(HttpMethod.Patch, EditPath(taskId), TokenFor(stranger), EditBody(body, Rule("daily")));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "personal isolation (FR-065)");
    }

    [Fact]
    public async Task Shared_project_editor_may_set_a_rule_viewer_403_non_member_404_former_member_404()
    {
        var owner = await CreateUserAsync("g-shared-o", "shared-o@example.com", "Owner");
        var editor = await CreateUserAsync("g-shared-e", "shared-e@example.com", "Editor");
        var viewer = await CreateUserAsync("g-shared-v", "shared-v@example.com", "Viewer");
        var outsider = await CreateUserAsync("g-shared-x", "shared-x@example.com", "Outsider");
        var former = await CreateUserAsync("g-shared-f", "shared-f@example.com", "Former");
        var ownerToken = TokenFor(owner);
        var project = await ShareProjectAsync(ownerToken, await CreateProjectAsync(ownerToken));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);
        await SeedMembershipAsync(project.Id, former, MembershipRoles.Editor);
        await DeleteMembershipRowAsync(project.Id, former);
        var taskId = await SeedRecurringTaskAsync(owner, "Shared chore", DueOnDay(0), null, projectId: project.Id);
        var body = await GetTaskBodyAsync(ownerToken, taskId);

        using (var denied = await SendAsync(HttpMethod.Patch, EditPath(taskId), TokenFor(viewer), EditBody(body, Rule("daily"))))
        {
            denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, "viewer = read only (FR-067)");
        }

        using (var denied = await SendAsync(HttpMethod.Patch, EditPath(taskId), TokenFor(outsider), EditBody(body, Rule("daily"))))
        {
            denied.StatusCode.Should().Be(HttpStatusCode.NotFound, "non-member (FR-066)");
        }

        using (var denied = await SendAsync(HttpMethod.Patch, EditPath(taskId), TokenFor(former), EditBody(body, Rule("daily"))))
        {
            denied.StatusCode.Should().Be(HttpStatusCode.NotFound, "former member loses ALL access (FR-066)");
        }

        using var allowed = await SendAsync(HttpMethod.Patch, EditPath(taskId), TokenFor(editor), EditBody(body, Rule("daily")));
        allowed.StatusCode.Should().Be(HttpStatusCode.OK, "editor = write (FR-067)");
        (await allowed.ReadTaskAsync()).RecurrenceRule!.Type.Should().Be("daily");
    }

    [Fact]
    public async Task Edit_with_a_stale_version_is_409()
    {
        var (_, token, taskId) = await SeedOwnedAsync("stale");
        var body = await GetTaskBodyAsync(token, taskId);
        var stale = body with { Version = body.Version + 7 };

        using var response = await SendAsync(HttpMethod.Patch, EditPath(taskId), token, EditBody(stale, Rule("daily")));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Edit_without_a_token_is_401()
    {
        var (_, token, taskId) = await SeedOwnedAsync("anon");
        var body = await GetTaskBodyAsync(token, taskId);

        using var request = new HttpRequestMessage(HttpMethod.Patch, EditPath(taskId))
        {
            Content = System.Net.Http.Json.JsonContent.Create(EditBody(body, Rule("daily"))),
        };
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
