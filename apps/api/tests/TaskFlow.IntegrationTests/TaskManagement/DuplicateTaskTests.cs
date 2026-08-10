using System.Net;
using FluentAssertions;
using TaskFlow.IntegrationTests.Infrastructure;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (slice 019, T030; FR-112, FR-065/FR-068) for
/// <c>POST /api/tasks/{id}/duplicate</c> (operationId <c>duplicateTask</c>,
/// contracts/task-duplicate.md). Copies title/description/priority/due date/labels/project/
/// assignees (assignees re-validated against CURRENT membership; non-members silently dropped);
/// NOT copied: completion state and comments. Fresh status/timestamps; position directly AFTER
/// the source; client-supplied <c>newTaskId</c> idempotency.
/// </summary>
public sealed class DuplicateTaskTests : SharingTestBase
{
    private static string DuplicatePath(Guid sourceId) => $"/api/tasks/{sourceId}/duplicate";

    [Fact]
    public async Task Allow_owner_duplicates_an_inbox_task_copying_fields_with_fresh_status_and_adjacent_position()
    {
        var owner = await CreateUserAsync("g-dp-a", "dpa@example.com", "Owner");
        var token = TokenFor(owner);
        var due = new DateTime(2026, 8, 20, 22, 0, 0, DateTimeKind.Utc);

        var sourceId = await SeedTaskAsync(owner, "Źródło", "a0", dueDate: due, dueHasTime: false, priority: "P1");
        var successorId = await SeedTaskAsync(owner, "Następnik", "a1");
        _ = successorId;

        var newId = Guid.CreateVersion7();
        using var response = await SendAsync(HttpMethod.Post, DuplicatePath(sourceId), token, new { newTaskId = newId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadTaskAsync();
        body.Id.Should().Be(newId, "the client-supplied id is the duplicate's identity");
        body.Title.Should().Be("Źródło");
        body.Priority.Should().Be("P1");
        body.DueDate.Should().Be(due);
        body.DueHasTime.Should().Be(false);
        body.Status.Should().Be("backlog", "the duplicate starts at the FR-003 default");
        body.CompletedAt.Should().BeNull();
        body.ProjectId.Should().BeNull("an Inbox source duplicates into the Inbox");

        // Adjacency (D7): strictly after the source, strictly before the successor (byte-ordinal).
        string.CompareOrdinal(body.Position, "a0").Should().BePositive();
        string.CompareOrdinal(body.Position, "a1").Should().BeNegative();
    }

    [Fact]
    public async Task Allow_completion_state_and_comments_are_not_copied()
    {
        var owner = await CreateUserAsync("g-dp-nc", "dpnc@example.com", "Owner");
        var member = await CreateUserAsync("g-dp-nc-m", "dpncm@example.com", "Member");
        var token = TokenFor(owner);

        var project = await CreateProjectAsync(token, name: "Komentowane");
        await ShareProjectAsync(token, project);
        await SeedMembershipAsync(project.Id, member, MembershipRoles.Editor);
        var sourceId = await SeedTaskAsync(owner, "Zrobione źródło", "a0", projectId: project.Id, done: true);

        using (var post = await SendAsync(
            HttpMethod.Post, $"/api/tasks/{sourceId}/comments", token,
            new { body = "Komentarz na źródle", mentionedUserIds = Array.Empty<Guid>() }))
        {
            post.StatusCode.Should().Be(HttpStatusCode.OK, "seeding a comment on the source");
        }

        var newId = Guid.CreateVersion7();
        using var response = await SendAsync(HttpMethod.Post, DuplicatePath(sourceId), token, new { newTaskId = newId });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadTaskAsync();
        body.Status.Should().Be("backlog", "completion state is NOT copied");
        body.CompletedAt.Should().BeNull();
        body.ProjectId.Should().Be(project.Id, "the duplicate stays in the source's context");

        using var comments = await SendAsync(HttpMethod.Get, $"/api/tasks/{newId}/comments", token);
        comments.StatusCode.Should().Be(HttpStatusCode.OK);
        (await comments.ReadCommentListAsync()).Comments.Should().BeEmpty("comments are NOT copied");
    }

    [Fact]
    public async Task Allow_an_editor_duplicates_a_shared_project_task_and_assignees_copy()
    {
        var owner = await CreateUserAsync("g-dp-ed-o", "dpedo@example.com", "Owner");
        var editor = await CreateUserAsync("g-dp-ed-e", "dpede@example.com", "Editor");
        var ownerToken = TokenFor(owner);

        var project = await CreateProjectAsync(ownerToken, name: "Wspólny");
        await ShareProjectAsync(ownerToken, project);
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        var sourceId = await SeedTaskAsync(owner, "Wspólne zadanie", "a0", projectId: project.Id);

        using (var assign = await SendAsync(
            HttpMethod.Patch, $"/api/tasks/{sourceId}/assignees", ownerToken,
            new { assigneeIds = new[] { editor.Value }, version = 0 }))
        {
            assign.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var newId = Guid.CreateVersion7();
        using var response = await SendAsync(
            HttpMethod.Post, DuplicatePath(sourceId), TokenFor(editor), new { newTaskId = newId });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "an editor can create in the shared context");
        var body = await response.ReadTaskAsync();
        body.ProjectId.Should().Be(project.Id);
        body.Assignees.Should().BeEquivalentTo([editor.Value], "current-member assignees carry over");
    }

    [Fact]
    public async Task Allow_an_ex_member_assignee_is_silently_dropped_from_the_copy()
    {
        var owner = await CreateUserAsync("g-dp-ex-o", "dpexo@example.com", "Owner");
        var exMember = await CreateUserAsync("g-dp-ex-x", "dpexx@example.com", "ExMember");
        var ownerToken = TokenFor(owner);

        var project = await CreateProjectAsync(ownerToken, name: "Rotacja");
        await ShareProjectAsync(ownerToken, project);
        await SeedMembershipAsync(project.Id, exMember, MembershipRoles.Editor);
        var sourceId = await SeedTaskAsync(owner, "Z przypisanym", "a0", projectId: project.Id);

        using (var assign = await SendAsync(
            HttpMethod.Patch, $"/api/tasks/{sourceId}/assignees", ownerToken,
            new { assigneeIds = new[] { exMember.Value }, version = 0 }))
        {
            assign.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Remove the membership row DIRECTLY (bypassing the event-driven assignment cleanup) so the
        // source still carries a stale ex-member assignee — the exact case the re-validation covers.
        await DeleteMembershipRowAsync(project.Id, exMember);

        var newId = Guid.CreateVersion7();
        using var response = await SendAsync(HttpMethod.Post, DuplicatePath(sourceId), ownerToken, new { newTaskId = newId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadTaskAsync();
        body.Assignees.Should().BeNullOrEmpty("the ex-member is silently dropped, no error");
    }

    [Fact]
    public async Task Allow_labels_of_the_caller_copy_to_the_duplicate()
    {
        var owner = await CreateUserAsync("g-dp-lb", "dplb@example.com", "Owner");
        var token = TokenFor(owner);
        var sourceId = await SeedTaskAsync(owner, "Z etykietami", "a0");

        var labelId = Guid.CreateVersion7();
        using (var createLabel = await SendAsync(HttpMethod.Put, $"/api/labels/{labelId}", token, new { name = "dom", color = "green" }))
        {
            createLabel.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var setLabels = await SendAsync(HttpMethod.Patch, $"/api/tasks/{sourceId}/labels", token, new { labelIds = new[] { labelId } }))
        {
            setLabels.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var newId = Guid.CreateVersion7();
        using var response = await SendAsync(HttpMethod.Post, DuplicatePath(sourceId), token, new { newTaskId = newId });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadTaskAsync()).Labels.Should().BeEquivalentTo([labelId]);
    }

    [Fact]
    public async Task Allow_idempotent_replay_returns_the_existing_duplicate_without_a_second_copy()
    {
        var owner = await CreateUserAsync("g-dp-idem", "dpidem@example.com", "Owner");
        var token = TokenFor(owner);
        var sourceId = await SeedTaskAsync(owner, "Powtórka", "a0");

        var newId = Guid.CreateVersion7();
        using (var first = await SendAsync(HttpMethod.Post, DuplicatePath(sourceId), token, new { newTaskId = newId }))
        {
            first.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var replay = await SendAsync(HttpMethod.Post, DuplicatePath(sourceId), token, new { newTaskId = newId });
        replay.StatusCode.Should().Be(HttpStatusCode.OK, "replay with the same newTaskId is a no-op success");
        (await replay.ReadTaskAsync()).Id.Should().Be(newId);

        using var list = await SendAsync(HttpMethod.Get, "/api/tasks", token);
        (await list.ReadTasksAsync()).Count(t => t.Title == "Powtórka").Should().Be(2, "source + ONE duplicate, never two");
    }

    [Fact]
    public async Task Conflict_a_newTaskId_that_exists_but_is_not_a_duplicate_of_this_source_is_409()
    {
        var owner = await CreateUserAsync("g-dp-conf", "dpconf@example.com", "Owner");
        var token = TokenFor(owner);
        var sourceId = await SeedTaskAsync(owner, "Prawdziwe źródło", "a0");
        var unrelatedId = await SeedTaskAsync(owner, "Zupełnie inne zadanie", "a1");

        using var response = await SendAsync(HttpMethod.Post, DuplicatePath(sourceId), token, new { newTaskId = unrelatedId });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Conflict_a_foreign_newTaskId_is_409()
    {
        var owner = await CreateUserAsync("g-dp-f-o", "dpfo@example.com", "Owner");
        var other = await CreateUserAsync("g-dp-f-x", "dpfx@example.com", "Other");
        var sourceId = await SeedTaskAsync(owner, "Moje źródło", "a0");
        var foreignId = await SeedTaskAsync(other, "Cudze zadanie", "a0");

        using var response = await SendAsync(
            HttpMethod.Post, DuplicatePath(sourceId), TokenFor(owner), new { newTaskId = foreignId });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "the id is taken; existence is not disclosed beyond the conflict");
    }

    [Fact]
    public async Task BadRequest_missing_or_malformed_newTaskId_is_400()
    {
        var owner = await CreateUserAsync("g-dp-400", "dp400@example.com", "Owner");
        var token = TokenFor(owner);
        var sourceId = await SeedTaskAsync(owner, "Do walidacji", "a0");

        using (var missing = await SendAsync(HttpMethod.Post, DuplicatePath(sourceId), token, new { }))
        {
            missing.StatusCode.Should().Be(HttpStatusCode.BadRequest, "newTaskId is required");
        }

        using var malformed = await SendAsync(
            HttpMethod.Post, DuplicatePath(sourceId), token, new { newTaskId = "not-a-guid" });
        malformed.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a malformed uuid never binds");
    }

    [Fact]
    public async Task Deny_a_non_member_gets_a_404_shaped_denial()
    {
        var owner = await CreateUserAsync("g-dp-nm-o", "dpnmo@example.com", "Owner");
        var outsider = await CreateUserAsync("g-dp-nm-x", "dpnmx@example.com", "Outsider");
        var ownerToken = TokenFor(owner);

        var project = await CreateProjectAsync(ownerToken, name: "Zamknięty");
        await ShareProjectAsync(ownerToken, project);
        var sourceId = await SeedTaskAsync(owner, "Niedostępne", "a0", projectId: project.Id);

        using var response = await SendAsync(
            HttpMethod.Post, DuplicatePath(sourceId), TokenFor(outsider), new { newTaskId = Guid.CreateVersion7() });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "inaccessible reads as nonexistent");
    }

    [Fact]
    public async Task Deny_a_viewer_role_member_is_create_denied_403()
    {
        var owner = await CreateUserAsync("g-dp-vw-o", "dpvwo@example.com", "Owner");
        var viewer = await CreateUserAsync("g-dp-vw-v", "dpvwv@example.com", "Viewer");
        var ownerToken = TokenFor(owner);

        var project = await CreateProjectAsync(ownerToken, name: "Tylko odczyt");
        await ShareProjectAsync(ownerToken, project);
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);
        var sourceId = await SeedTaskAsync(owner, "Widoczne", "a0", projectId: project.Id);

        using var response = await SendAsync(
            HttpMethod.Post, DuplicatePath(sourceId), TokenFor(viewer), new { newTaskId = Guid.CreateVersion7() });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a viewer cannot create tasks in the project");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
    }

    [Fact]
    public async Task Deny_another_users_inbox_task_is_404()
    {
        var owner = await CreateUserAsync("g-dp-xi-o", "dpxio@example.com", "Owner");
        var stranger = await CreateUserAsync("g-dp-xi-s", "dpxis@example.com", "Stranger");
        var sourceId = await SeedTaskAsync(owner, "Prywatne", "a0");

        using var response = await SendAsync(
            HttpMethod.Post, DuplicatePath(sourceId), TokenFor(stranger), new { newTaskId = Guid.CreateVersion7() });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
