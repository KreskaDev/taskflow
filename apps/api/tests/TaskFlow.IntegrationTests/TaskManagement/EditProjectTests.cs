using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T014, US1) for <c>PATCH /api/projects/{id}</c> — the whole-object editProject
/// endpoint (research R4): name/color/icon/parentId are updated together, parentId is REQUIRED (never a
/// silent un-parent), and the optimistic <c>version</c> guards the write. Re-parenting is just supplying
/// a different parentId; it triggers the 404-before-422 ownership + one-level-nesting precedence (R3/R13,
/// AS-08/AS-09).
/// </summary>
/// <remarks>
/// RED-FIRST (Constitution VIII): this file compiles and FAILS until T015 (command + handler + validator
/// + the PATCH route) lands. The route does not exist yet, so the allow cases expect 200 but observe 404.
/// </remarks>
public sealed class EditProjectTests : IntegrationTestBase
{
    private const string EnsurePath = "/api/users/ensure";

    private static string CreatePath(Guid id) => $"/api/projects/{id}";

    private static string EditPath(Guid id) => $"/api/projects/{id}";

    private async Task<UserId> CreateOwnerAsync(string sub, string email)
    {
        var profile = await (await SendAsync(
            HttpMethod.Post, EnsurePath, TestJwtHelper.Valid(sub),
            new { googleSubjectId = sub, email, displayName = "Project Owner", avatarUrl = (string?)null }))
            .ReadProfileAsync();
        return UserId.From(profile.Id);
    }

    private async Task<ProjectBody> CreateProjectAsync(string token, string name, string color, string icon, Guid? parentId = null)
    {
        var id = Guid.CreateVersion7();
        using var response = await SendAsync(HttpMethod.Put, CreatePath(id), token, new { name, color, icon, parentId });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the helper creates a valid project");
        return await response.ReadProjectAsync();
    }

    [Fact]
    public async Task Allow_edits_name_color_icon_together_and_bumps_version()
    {
        var owner = await CreateOwnerAsync("google-sub-ep-edit", "epedit@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var project = await CreateProjectAsync(token, "Old", "blue", "folder");

        using var response = await SendAsync(
            HttpMethod.Patch, EditPath(project.Id), token,
            new { name = "New", color = "red", icon = "star", parentId = (Guid?)null, version = project.Version });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadProjectAsync();
        body.Name.Should().Be("New");
        body.Color.Should().Be("red");
        body.Icon.Should().Be("star");
        body.ParentId.Should().BeNull();
        body.Version.Should().Be(project.Version + 1, "an edit bumps the optimistic-concurrency token");

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Projects.SingleAsync(p => p.Id == ProjectId.From(project.Id));
        stored.Name.Should().Be("New");
        stored.Color.Should().Be("red");
        stored.Icon.Should().Be("star");
    }

    [Fact]
    public async Task Allow_reparent_under_an_owned_top_level_parent()
    {
        // AS-08: re-parenting a top-level project under another owned top-level project is allowed.
        var owner = await CreateOwnerAsync("google-sub-ep-reparent", "epreparent@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var parent = await CreateProjectAsync(token, "Parent", "green", "folder");
        var child = await CreateProjectAsync(token, "Child", "teal", "star");

        using var response = await SendAsync(
            HttpMethod.Patch, EditPath(child.Id), token,
            new { name = child.Name, color = child.Color, icon = child.Icon, parentId = (Guid?)parent.Id, version = child.Version });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "re-parenting under an owned top-level parent is allowed (AS-08)");
        (await response.ReadProjectAsync()).ParentId.Should().Be(parent.Id);
    }

    [Fact]
    public async Task Reject_reparent_under_a_child_is_422_grandchild()
    {
        // AS-09 failure shape 1: the target parent is itself a child → would create a grandchild → 422.
        var owner = await CreateOwnerAsync("google-sub-ep-grandchild", "epgrandchild@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var top = await CreateProjectAsync(token, "Top", "blue", "folder");
        var middle = await CreateProjectAsync(token, "Middle", "green", "star", parentId: top.Id);
        var loose = await CreateProjectAsync(token, "Loose", "red", "flag");

        using var response = await SendAsync(
            HttpMethod.Patch, EditPath(loose.Id), token,
            new { name = loose.Name, color = loose.Color, icon = loose.Icon, parentId = (Guid?)middle.Id, version = loose.Version });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "nesting under a child would create a grandchild (FR-012)");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");
        problem.Errors!.Keys.Should().Contain(k => k.Equals("parentId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Reject_reparent_a_project_that_has_children_is_422()
    {
        // AS-09 failure shape 2: the project being re-parented has its OWN children → giving it a parent
        // would push its children to depth 2 → 422.
        var owner = await CreateOwnerAsync("google-sub-ep-haschildren", "ephaschildren@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var newParent = await CreateProjectAsync(token, "New parent", "blue", "folder");
        var parent = await CreateProjectAsync(token, "Parent with child", "green", "folder");
        _ = await CreateProjectAsync(token, "Its child", "teal", "star", parentId: parent.Id);

        using var response = await SendAsync(
            HttpMethod.Patch, EditPath(parent.Id), token,
            new { name = parent.Name, color = parent.Color, icon = parent.Icon, parentId = (Guid?)newParent.Id, version = parent.Version });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a project with children cannot itself be nested (FR-012)");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");
        problem.Errors!.Keys.Should().Contain(k => k.Equals("parentId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ForeignParent_reparent_under_a_foreign_target_is_404_before_nesting()
    {
        // R3/R13: a foreign target parent → 404 (existence not disclosed), BEFORE the nesting 422.
        var owner = await CreateOwnerAsync("google-sub-ep-fp-owner", "epfpowner@example.com");
        var stranger = await CreateOwnerAsync("google-sub-ep-fp-stranger", "epfpstranger@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var strangerParent = await CreateProjectAsync(TestJwtHelper.Valid(stranger.Value.ToString()), "Stranger's", "blue", "folder");
        var project = await CreateProjectAsync(token, "Mine", "green", "folder");

        using var response = await SendAsync(
            HttpMethod.Patch, EditPath(project.Id), token,
            new { name = project.Name, color = project.Color, icon = project.Icon, parentId = (Guid?)strangerParent.Id, version = project.Version });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a foreign target parent is 404, never 422 — no enumeration oracle");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Stale_version_is_rejected_409_version_conflict()
    {
        var owner = await CreateOwnerAsync("google-sub-ep-stale", "epstale@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var project = await CreateProjectAsync(token, "Project", "blue", "folder");

        using var response = await SendAsync(
            HttpMethod.Patch, EditPath(project.Id), token,
            new { name = "New", color = "red", icon = "star", parentId = (Guid?)null, version = project.Version + 99 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "a stale version is rejected with 409 version_conflict (R4)");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("version_conflict");
        problem.Status.Should().Be(409);
    }

    [Fact]
    public async Task WholeObjectReplace_omitted_required_parentId_is_rejected_never_a_silent_unparent()
    {
        // R4 whole-object replace: parentId is REQUIRED on EditProjectRequest. A name-only edit that OMITS
        // parentId is LOUDLY rejected, NEVER a silent demotion of a child to top-level. The contract prose
        // says "422", but in this stack a missing System.Text.Json `required` member is a binding-layer 400
        // (the app-wide behavior for every omitted required DTO field — e.g. CreateTaskRequest.Title,
        // RenameTaskRequest.Version — so EditProjectRequest stays consistent with the Task vertical). No new
        // error code is introduced (R12); the load-bearing invariant is "rejected + row untouched", which a
        // 400 satisfies exactly. The 400 envelope may carry no errorCode, so this asserts the status + the
        // row-untouched crux, not ReadProblemAsync.
        var owner = await CreateOwnerAsync("google-sub-ep-omitparent", "epomitparent@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var parent = await CreateProjectAsync(token, "Parent", "blue", "folder");
        var child = await CreateProjectAsync(token, "Child", "green", "star", parentId: parent.Id);

        // The body omits parentId entirely (no key) — the required field is absent.
        using var response = await SendAsync(
            HttpMethod.Patch, EditPath(child.Id), token,
            new { name = "Renamed", color = child.Color, icon = child.Icon, version = child.Version });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "an omitted required parentId is loudly rejected at binding, never a silent un-parent (R4)");

        // The child's parent is UNTOUCHED — the rejected edit never demoted it (the load-bearing invariant).
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Projects.SingleAsync(p => p.Id == ProjectId.From(child.Id));
        stored.ParentId.Should().Be(ProjectId.From(parent.Id), "the rejected edit never un-parented the child");
        stored.Name.Should().Be("Child", "the rejected edit never applied the rename either");
    }

    [Fact]
    public async Task Deny_editing_another_users_project_is_404_not_found()
    {
        // R13 DENY: editing a project owned by a DIFFERENT user is 404 (NOT 403), and leaves it untouched.
        var owner = await CreateOwnerAsync("google-sub-ep-deny-owner", "epdenyowner@example.com");
        var stranger = await CreateOwnerAsync("google-sub-ep-deny-stranger", "epdenystranger@example.com");
        var project = await CreateProjectAsync(TestJwtHelper.Valid(owner.Value.ToString()), "Owner's", "blue", "folder");

        using var response = await SendAsync(
            HttpMethod.Patch, EditPath(project.Id), TestJwtHelper.Valid(stranger.Value.ToString()),
            new { name = "Hijack", color = "red", icon = "star", parentId = (Guid?)null, version = project.Version });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a foreign id is not_found (404), never 403");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Projects.SingleAsync(p => p.Id == ProjectId.From(project.Id));
        stored.Name.Should().Be("Owner's", "the foreign edit never mutated the owner's row");
    }

    [Fact]
    public async Task Deny_no_jwt_is_rejected_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri(EditPath(Guid.CreateVersion7()), UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { name = "X", color = "blue", icon = "folder", parentId = (Guid?)null, version = 0 }),
        };
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "editProject is deny-by-default (FR-068)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
    }
}
