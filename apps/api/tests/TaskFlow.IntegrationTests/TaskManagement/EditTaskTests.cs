using System.Net;
using FluentAssertions;
using TaskFlow.IntegrationTests.Infrastructure;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T012/T042, US1) for <c>PATCH /api/tasks/{id}/edit</c> (operationId <c>editTask</c>,
/// slice 005, AS-06/07/08) — the combined whole-object-replace editor. Every editable field is saved
/// together atomically; an omitted required key is loudly rejected at binding (400, the app-wide behavior for
/// a missing System.Text.Json <c>required</c> member — same as slice-004 <c>EditProject</c>), never a silent
/// null. Authorization is dispatched by the containing project's visibility (personal → ownership; shared →
/// editor/owner). The project field reuses the move-to-project ownership check, but ONLY on an actual move.
/// </summary>
public sealed class EditTaskTests : SharingTestBase
{
    private static readonly DateTime ValidDue = new(2026, 8, 2, 13, 0, 0, DateTimeKind.Utc);

    private static string EditPath(Guid id) => $"/api/tasks/{id}/edit";

    [Fact]
    public async Task Allow_whole_object_replace_saves_every_field_together()
    {
        var owner = await CreateUserAsync("g-ed-a", "eda@example.com", "Owner");
        var id = await SeedTaskAsync(owner, "Original");

        using var response = await SendAsync(HttpMethod.Patch, EditPath(id), TokenFor(owner), new
        {
            title = "Edited",
            description = "Now with detail",
            priority = "P1",
            dueDate = ValidDue,
            dueHasTime = true,
            projectId = (Guid?)null,
            version = 0,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadTaskAsync();
        body.Title.Should().Be("Edited");
        body.Description.Should().Be("Now with detail");
        body.Priority.Should().Be("P1");
        body.DueDate.Should().Be(ValidDue);
        body.DueHasTime.Should().BeTrue();
        body.Version.Should().Be(1, "the whole-object replace is a single mutation");
    }

    [Fact]
    public async Task Allow_moving_into_an_owned_project_on_edit()
    {
        var owner = await CreateUserAsync("g-ed-mv", "edmv@example.com", "Owner");
        var token = TokenFor(owner);
        var project = await CreateProjectAsync(token, name: "Target");
        var id = await SeedTaskAsync(owner, "Move me");

        using var response = await SendAsync(HttpMethod.Patch, EditPath(id), token, new
        {
            title = "Move me",
            description = (string?)null,
            priority = (string?)null,
            dueDate = (DateTime?)null,
            dueHasTime = (bool?)null,
            projectId = (Guid?)project.Id,
            version = 0,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadTaskAsync()).ProjectId.Should().Be(project.Id);
    }

    [Fact]
    public async Task Deny_moving_into_a_foreign_project_on_edit_is_404()
    {
        var owner = await CreateUserAsync("g-ed-fo", "edfo@example.com", "Owner");
        var other = await CreateUserAsync("g-ed-ot", "edot@example.com", "Other");
        var foreignProject = await CreateProjectAsync(TokenFor(other), name: "Theirs");
        var id = await SeedTaskAsync(owner, "Mine");

        using var response = await SendAsync(HttpMethod.Patch, EditPath(id), TokenFor(owner), new
        {
            title = "Mine",
            description = (string?)null,
            priority = (string?)null,
            dueDate = (DateTime?)null,
            dueHasTime = (bool?)null,
            projectId = (Guid?)foreignProject.Id,
            version = 0,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a task can never be filed under another user's project");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_an_omitted_required_key_is_rejected_at_binding_and_leaves_the_row_untouched()
    {
        // Whole-object replace (R4): an omitted required key is loudly rejected, never a silent null. In this
        // stack a missing System.Text.Json `required` member is a binding-layer 400 (the app-wide behavior —
        // same as slice-004 EditProjectRequest.parentId and CreateTaskRequest.Title). The load-bearing
        // invariant is "rejected + row untouched", which a 400 satisfies exactly. No new error code (R11).
        var owner = await CreateUserAsync("g-ed-omit", "edomit@example.com", "Owner");
        var id = await SeedTaskAsync(owner, "Keep me");

        // The body OMITS `priority` entirely (no key).
        using var response = await SendAsync(HttpMethod.Patch, EditPath(id), TokenFor(owner), new
        {
            title = "Renamed",
            description = (string?)null,
            dueDate = (DateTime?)null,
            dueHasTime = (bool?)null,
            projectId = (Guid?)null,
            version = 0,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "an omitted required key is loudly rejected at binding (R4)");
        var stored = await LoadTaskAsync(id);
        stored!.Title.Should().Be("Keep me", "the rejected edit never applied");
    }

    [Theory]
    [InlineData("P9")]
    [InlineData("p1")]
    public async Task Deny_an_out_of_set_priority_is_422(string priority)
    {
        var owner = await CreateUserAsync($"g-ed-pri-{priority}", $"edpri{priority}@example.com", "Owner");
        var id = await SeedTaskAsync(owner, "Closed-set guarded");

        using var response = await SendAsync(HttpMethod.Patch, EditPath(id), TokenFor(owner), new
        {
            title = "T",
            description = (string?)null,
            priority,
            dueDate = (DateTime?)null,
            dueHasTime = (bool?)null,
            projectId = (Guid?)null,
            version = 0,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task Deny_a_half_set_due_pair_is_422()
    {
        var owner = await CreateUserAsync("g-ed-pair", "edpair@example.com", "Owner");
        var id = await SeedTaskAsync(owner, "Pairing-guarded");

        using var response = await SendAsync(HttpMethod.Patch, EditPath(id), TokenFor(owner), new
        {
            title = "T",
            description = (string?)null,
            priority = (string?)null,
            dueDate = ValidDue,
            dueHasTime = (bool?)null,
            projectId = (Guid?)null,
            version = 0,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task Deny_an_over_long_description_is_422()
    {
        var owner = await CreateUserAsync("g-ed-desc", "eddesc@example.com", "Owner");
        var id = await SeedTaskAsync(owner, "Length-guarded");

        using var response = await SendAsync(HttpMethod.Patch, EditPath(id), TokenFor(owner), new
        {
            title = "T",
            description = new string('x', 8001),
            priority = (string?)null,
            dueDate = (DateTime?)null,
            dueHasTime = (bool?)null,
            projectId = (Guid?)null,
            version = 0,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task Deny_a_stale_version_is_409()
    {
        var owner = await CreateUserAsync("g-ed-stale", "edstale@example.com", "Owner");
        var id = await SeedTaskAsync(owner, "Concurrency-guarded");

        using var response = await SendAsync(HttpMethod.Patch, EditPath(id), TokenFor(owner), new
        {
            title = "T",
            description = (string?)null,
            priority = (string?)null,
            dueDate = (DateTime?)null,
            dueHasTime = (bool?)null,
            projectId = (Guid?)null,
            version = 5,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("version_conflict");
    }

    [Fact]
    public async Task Deny_another_users_personal_task_is_404()
    {
        var owner = await CreateUserAsync("g-ed-o", "edo@example.com", "Owner");
        var stranger = await CreateUserAsync("g-ed-x", "edx@example.com", "Stranger");
        var id = await SeedTaskAsync(owner, "Owner's task");

        using var response = await SendAsync(HttpMethod.Patch, EditPath(id), TokenFor(stranger), new
        {
            title = "Hijack",
            description = (string?)null,
            priority = (string?)null,
            dueDate = (DateTime?)null,
            dueHasTime = (bool?)null,
            projectId = (Guid?)null,
            version = 0,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Allow_an_editor_member_edits_a_shared_task_with_unchanged_project()
    {
        // The projectId-trap: the editor sends the CURRENT (shared) projectId — an UNCHANGED target is NOT a
        // move, so the owner-scoped target check is SKIPPED and the legitimate edit is not spuriously 404'd.
        var owner = await CreateUserAsync("g-ed-so", "edso@example.com", "Owner");
        var editor = await CreateUserAsync("g-ed-sed", "edsed@example.com", "Editor");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);

        using var response = await SendAsync(HttpMethod.Patch, EditPath(id), TokenFor(editor), new
        {
            title = "Edited by editor",
            description = "edited",
            priority = "P2",
            dueDate = (DateTime?)null,
            dueHasTime = (bool?)null,
            projectId = (Guid?)project.Id, // unchanged → no move → no target ownership check
            version = 0,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "an editor edits a shared task in place without being 404'd by the move check");
        var body = await response.ReadTaskAsync();
        body.Title.Should().Be("Edited by editor");
        body.ProjectId.Should().Be(project.Id);
    }

    [Fact]
    public async Task Deny_a_viewer_member_editing_a_shared_task_is_403()
    {
        var owner = await CreateUserAsync("g-ed-vo", "edvo@example.com", "Owner");
        var viewer = await CreateUserAsync("g-ed-vw", "edvw@example.com", "Viewer");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);

        using var response = await SendAsync(HttpMethod.Patch, EditPath(id), TokenFor(viewer), new
        {
            title = "Viewer edit",
            description = (string?)null,
            priority = (string?)null,
            dueDate = (DateTime?)null,
            dueHasTime = (bool?)null,
            projectId = (Guid?)project.Id,
            version = 0,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
    }

    [Fact]
    public async Task Deny_a_non_member_editing_a_shared_task_is_404()
    {
        var owner = await CreateUserAsync("g-ed-no", "edno@example.com", "Owner");
        var stranger = await CreateUserAsync("g-ed-nx", "ednx@example.com", "Stranger");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);

        using var response = await SendAsync(HttpMethod.Patch, EditPath(id), TokenFor(stranger), new
        {
            title = "Stranger edit",
            description = (string?)null,
            priority = (string?)null,
            dueDate = (DateTime?)null,
            dueHasTime = (bool?)null,
            projectId = (Guid?)project.Id,
            version = 0,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }
}
