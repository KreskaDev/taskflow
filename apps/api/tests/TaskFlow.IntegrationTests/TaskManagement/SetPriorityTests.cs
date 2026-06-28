using System.Net;
using FluentAssertions;
using TaskFlow.IntegrationTests.Infrastructure;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T008/T042, US1) for <c>PATCH /api/tasks/{id}/priority</c> (operationId
/// <c>setTaskPriority</c>, slice 005, AS-04). The body is <c>{ priority: "P0".."P3"|null, version }</c>.
/// Authorization is dispatched by the containing project's visibility (research R2/R10): personal →
/// ownership (foreign → 404); shared → editor/owner (a viewer member → 403, a non-member → 404).
/// </summary>
public sealed class SetPriorityTests : SharingTestBase
{
    private static string PriorityPath(Guid id) => $"/api/tasks/{id}/priority";

    [Theory]
    [InlineData("P0")]
    [InlineData("P1")]
    [InlineData("P2")]
    [InlineData("P3")]
    public async Task Allow_owner_sets_priority_on_a_personal_task(string priority)
    {
        var owner = await CreateUserAsync($"g-sp-{priority}", $"sp{priority}@example.com", "Owner");
        var id = await SeedTaskAsync(owner, "Triage me");

        using var response = await SendAsync(HttpMethod.Patch, PriorityPath(id), TokenFor(owner), new { priority, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadTaskAsync();
        body.Priority.Should().Be(priority);
        body.Version.Should().Be(1, "a mutating write bumps the optimistic-concurrency token");
        (await LoadTaskAsync(id))!.Priority.Should().Be(priority);
    }

    [Fact]
    public async Task Allow_owner_clears_priority_with_null()
    {
        var owner = await CreateUserAsync("g-sp-clr", "spclr@example.com", "Owner");
        var id = await SeedTaskAsync(owner, "Already prioritized", priority: "P0");

        using var response = await SendAsync(HttpMethod.Patch, PriorityPath(id), TokenFor(owner), new { priority = (string?)null, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadTaskAsync()).Priority.Should().BeNull("null clears the priority");
    }

    [Theory]
    [InlineData("p0")]
    [InlineData("P4")]
    [InlineData("urgent")]
    public async Task Deny_an_out_of_set_priority_is_422(string priority)
    {
        var owner = await CreateUserAsync($"g-sp-bad-{priority}", $"spbad{priority}@example.com", "Owner");
        var id = await SeedTaskAsync(owner, "Closed-set guarded");

        using var response = await SendAsync(HttpMethod.Patch, PriorityPath(id), TokenFor(owner), new { priority, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
        (await LoadTaskAsync(id))!.Priority.Should().BeNull("the rejected set never landed");
    }

    [Fact]
    public async Task Deny_a_stale_version_is_409()
    {
        var owner = await CreateUserAsync("g-sp-stale", "spstale@example.com", "Owner");
        var id = await SeedTaskAsync(owner, "Concurrency-guarded");

        using var response = await SendAsync(HttpMethod.Patch, PriorityPath(id), TokenFor(owner), new { priority = "P0", version = 7 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("version_conflict");
    }

    [Fact]
    public async Task Deny_another_users_personal_task_is_404()
    {
        var owner = await CreateUserAsync("g-sp-o", "spo@example.com", "Owner");
        var stranger = await CreateUserAsync("g-sp-x", "spx@example.com", "Stranger");
        var id = await SeedTaskAsync(owner, "Owner's task");

        using var response = await SendAsync(HttpMethod.Patch, PriorityPath(id), TokenFor(stranger), new { priority = "P0", version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a foreign personal task is not_found, never 403");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
        (await LoadTaskAsync(id))!.Priority.Should().BeNull("the foreign write never mutated the owner's task");
    }

    [Fact]
    public async Task Allow_an_editor_member_sets_priority_on_a_shared_task()
    {
        var owner = await CreateUserAsync("g-sp-so", "spso@example.com", "Owner");
        var editor = await CreateUserAsync("g-sp-sed", "spsed@example.com", "Editor");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);

        using var response = await SendAsync(HttpMethod.Patch, PriorityPath(id), TokenFor(editor), new { priority = "P1", version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "an editor member may write to a shared-project task (FR-067)");
        (await response.ReadTaskAsync()).Priority.Should().Be("P1");
    }

    [Fact]
    public async Task Deny_a_viewer_member_is_403()
    {
        // SC-016 viewer-mutation-deny: a member, so existence is known → 403 (not 404).
        var owner = await CreateUserAsync("g-sp-vo", "spvo@example.com", "Owner");
        var viewer = await CreateUserAsync("g-sp-vw", "spvw@example.com", "Viewer");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);

        using var response = await SendAsync(HttpMethod.Patch, PriorityPath(id), TokenFor(viewer), new { priority = "P0", version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a viewer is a member but lacks write role (FR-067)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
        (await LoadTaskAsync(id))!.Priority.Should().BeNull("the viewer's write never landed");
    }

    [Fact]
    public async Task Deny_a_non_member_of_a_shared_project_is_404()
    {
        var owner = await CreateUserAsync("g-sp-no", "spno@example.com", "Owner");
        var stranger = await CreateUserAsync("g-sp-nx", "spnx@example.com", "Stranger");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        var id = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);

        using var response = await SendAsync(HttpMethod.Patch, PriorityPath(id), TokenFor(stranger), new { priority = "P0", version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a non-member is not told the shared task exists");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_no_jwt_is_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri(PriorityPath(Guid.CreateVersion7()), UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { priority = "P0", version = 0 }),
        };
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "set-priority is deny-by-default (FR-068)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
    }
}
