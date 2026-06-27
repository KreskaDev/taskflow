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
/// Allow + deny coverage (T012, US1) for <c>PUT /api/projects/{id}</c> — the idempotent,
/// insert-if-not-exists createProject endpoint keyed on the client-generated UUIDv7 <c>{id}</c>
/// (FR-001, research.md R2/R13). Mirrors <see cref="CreateTaskTests"/>: the owner is admitted via
/// <c>POST /api/users/ensure</c> and a carrier is minted with the returned <c>profile.Id</c>.
/// </summary>
/// <remarks>
/// RED-FIRST (Constitution VIII): this file compiles and FAILS until T013 (command + handler +
/// validator + the PUT route) lands. The PUT route does not exist yet, so the allow cases expect
/// <c>200</c> but observe <c>404</c>. The sharpest cases: the idempotent replay (R2 — a second PUT
/// of the same id+owner with DIFFERENT fields returns the row UNCHANGED, proving insert-if-not-exists
/// is not a blind upsert); the 404-before-422 parent error split (R3/R13 — a foreign parent → 404,
/// an owned-but-illegal parent → 422); and the foreign-owner DENY (404).
/// </remarks>
public sealed class CreateProjectTests : IntegrationTestBase
{
    private const string EnsurePath = "/api/users/ensure";

    private static string ProjectPath(Guid id) => $"/api/projects/{id}";

    private async Task<UserId> CreateOwnerAsync(string sub, string email)
    {
        var profile = await (await SendAsync(
            HttpMethod.Post, EnsurePath, TestJwtHelper.Valid(sub),
            new { googleSubjectId = sub, email, displayName = "Project Owner", avatarUrl = (string?)null }))
            .ReadProfileAsync();
        return UserId.From(profile.Id);
    }

    /// <summary>Creates a project via the PUT endpoint and returns the decoded body (helper for the parent cases).</summary>
    private async Task<ProjectBody> PutProjectAsync(string token, Guid id, string name, string color, string icon, Guid? parentId = null)
    {
        using var response = await SendAsync(
            HttpMethod.Put, ProjectPath(id), token,
            new { name, color, icon, parentId });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the helper creates a valid project");
        return await response.ReadProjectAsync();
    }

    [Fact]
    public async Task Allow_creates_a_new_top_level_project_with_default_fields()
    {
        var owner = await CreateOwnerAsync("google-sub-cp-200", "cp200@example.com");
        var id = Guid.CreateVersion7();

        using var response = await SendAsync(
            HttpMethod.Put, ProjectPath(id), TestJwtHelper.Valid(owner.Value.ToString()),
            new { name = "Work", color = "blue", icon = "briefcase", parentId = (Guid?)null });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.ReadProjectAsync();
        body.Id.Should().Be(id, "the client-minted UUIDv7 id is the id stored and returned");
        body.Name.Should().Be("Work");
        body.Color.Should().Be("blue");
        body.Icon.Should().Be("briefcase");
        body.ParentId.Should().BeNull("a top-level project has no parent");
        body.Visibility.Should().Be("personal", "a fresh project defaults to personal (R11)");
        body.ArchivedAt.Should().BeNull("a fresh project is not archived");
        body.Version.Should().Be(0, "a fresh Create starts version at 0 (no mutating behavior ran)");

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Projects.SingleAsync(p => p.Id == ProjectId.From(id));
        stored.OwnerId.Should().Be(owner, "ownerId is the authenticated caller (R13), never the wire");
        stored.Name.Should().Be("Work");
        stored.Visibility.Should().Be("personal");
        stored.DeletedAt.Should().BeNull();
        stored.CreatedAt.Should().NotBe(default);
        stored.UpdatedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task Allow_creates_a_child_under_an_owned_top_level_parent()
    {
        var owner = await CreateOwnerAsync("google-sub-cp-child", "cpchild@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var parent = await PutProjectAsync(token, Guid.CreateVersion7(), "Parent", "green", "folder");
        var childId = Guid.CreateVersion7();

        using var response = await SendAsync(
            HttpMethod.Put, ProjectPath(childId), token,
            new { name = "Child", color = "teal", icon = "star", parentId = (Guid?)parent.Id });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "nesting one level under an owned top-level parent is allowed (AS-02)");
        (await response.ReadProjectAsync()).ParentId.Should().Be(parent.Id);
    }

    [Fact]
    public async Task Allow_idempotent_replay_returns_the_existing_row_unchanged_and_does_not_bump_version()
    {
        // R2: a same-id+same-owner retry is an idempotent replay, NOT a replace.
        var owner = await CreateOwnerAsync("google-sub-cp-replay", "cpreplay@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var id = Guid.CreateVersion7();

        using (var first = await SendAsync(
            HttpMethod.Put, ProjectPath(id), token, new { name = "Original", color = "blue", icon = "folder", parentId = (Guid?)null }))
        {
            first.StatusCode.Should().Be(HttpStatusCode.OK);
            (await first.ReadProjectAsync()).Version.Should().Be(0);
        }

        using var replay = await SendAsync(
            HttpMethod.Put, ProjectPath(id), token, new { name = "DIFFERENT", color = "red", icon = "star", parentId = (Guid?)null });

        replay.StatusCode.Should().Be(HttpStatusCode.OK, "a same-owner retry is a SUCCESS (idempotent), never a 409/422");
        var replayed = await replay.ReadProjectAsync();
        replayed.Name.Should().Be("Original", "the replay returns the existing row UNCHANGED — create does not replace");
        replayed.Color.Should().Be("blue", "the original color is preserved, not overwritten by the replay payload");
        replayed.Icon.Should().Be("folder");
        replayed.Version.Should().Be(0, "an idempotent replay does NOT bump the optimistic-concurrency token");

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Projects.SingleAsync(p => p.Id == ProjectId.From(id));
        stored.Name.Should().Be("Original", "insert-if-not-exists never replaces the stored row on replay");
        stored.Version.Should().Be(0);
    }

    [Fact]
    public async Task Validation_empty_name_is_rejected_422()
    {
        var owner = await CreateOwnerAsync("google-sub-cp-emptyname", "cpemptyname@example.com");

        using var response = await SendAsync(
            HttpMethod.Put, ProjectPath(Guid.CreateVersion7()), TestJwtHelper.Valid(owner.Value.ToString()),
            new { name = "", color = "blue", icon = "folder", parentId = (Guid?)null });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");
        problem.Status.Should().Be(422);
        problem.Errors!.Keys.Should().Contain(k => k.Equals("name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validation_name_over_200_chars_is_rejected_422()
    {
        var owner = await CreateOwnerAsync("google-sub-cp-longname", "cplongname@example.com");

        using var response = await SendAsync(
            HttpMethod.Put, ProjectPath(Guid.CreateVersion7()), TestJwtHelper.Valid(owner.Value.ToString()),
            new { name = new string('a', 201), color = "blue", icon = "folder", parentId = (Guid?)null });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a >200 char name fails validation, not the domain guard");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");
        problem.Errors!.Keys.Should().Contain(k => k.Equals("name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validation_out_of_preset_color_is_rejected_422()
    {
        var owner = await CreateOwnerAsync("google-sub-cp-badcolor", "cpbadcolor@example.com");

        using var response = await SendAsync(
            HttpMethod.Put, ProjectPath(Guid.CreateVersion7()), TestJwtHelper.Valid(owner.Value.ToString()),
            new { name = "Work", color = "#ff00ff", icon = "folder", parentId = (Guid?)null });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "color must be a member of the frozen preset set (R10/ASM-04)");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");
        problem.Errors!.Keys.Should().Contain(k => k.Equals("color", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validation_out_of_preset_icon_is_rejected_422()
    {
        var owner = await CreateOwnerAsync("google-sub-cp-badicon", "cpbadicon@example.com");

        using var response = await SendAsync(
            HttpMethod.Put, ProjectPath(Guid.CreateVersion7()), TestJwtHelper.Valid(owner.Value.ToString()),
            new { name = "Work", color = "blue", icon = "skull", parentId = (Guid?)null });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "icon must be a member of the frozen preset set (R10/ASM-04)");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");
        problem.Errors!.Keys.Should().Contain(k => k.Equals("icon", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validation_self_parent_is_rejected_422()
    {
        // A project cannot be its own parent — a command-local check (FluentValidator), not a cross-row lookup.
        var owner = await CreateOwnerAsync("google-sub-cp-selfparent", "cpselfparent@example.com");
        var id = Guid.CreateVersion7();

        using var response = await SendAsync(
            HttpMethod.Put, ProjectPath(id), TestJwtHelper.Valid(owner.Value.ToString()),
            new { name = "Work", color = "blue", icon = "folder", parentId = (Guid?)id });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a project cannot be its own parent");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");
        problem.Errors!.Keys.Should().Contain(k => k.Equals("parentId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ForeignParent_absent_parent_is_rejected_404_before_any_nesting_check()
    {
        // R3/R13: an absent parentId → 404 (existence not disclosed), resolved BEFORE the nesting check.
        var owner = await CreateOwnerAsync("google-sub-cp-absentparent", "cpabsentparent@example.com");

        using var response = await SendAsync(
            HttpMethod.Put, ProjectPath(Guid.CreateVersion7()), TestJwtHelper.Valid(owner.Value.ToString()),
            new { name = "Work", color = "blue", icon = "folder", parentId = (Guid?)Guid.CreateVersion7() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "an absent parent is not_found (404), never 422 — no existence leak");
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("not_found");
        problem.Status.Should().Be(404);
    }

    [Fact]
    public async Task ForeignParent_parent_owned_by_another_user_is_rejected_404_before_any_nesting_check()
    {
        // R3/R13: a parent owned by a DIFFERENT user → 404 (existence not disclosed), NOT 422 (which would
        // leak the existence of another user's project — a Principle IX violation). The 404 strictly precedes
        // the nesting 422.
        var owner = await CreateOwnerAsync("google-sub-cp-fp-owner", "cpfpowner@example.com");
        var stranger = await CreateOwnerAsync("google-sub-cp-fp-stranger", "cpfpstranger@example.com");
        var strangerParent = await PutProjectAsync(
            TestJwtHelper.Valid(stranger.Value.ToString()), Guid.CreateVersion7(), "Stranger's parent", "blue", "folder");

        using var response = await SendAsync(
            HttpMethod.Put, ProjectPath(Guid.CreateVersion7()), TestJwtHelper.Valid(owner.Value.ToString()),
            new { name = "Work", color = "blue", icon = "folder", parentId = (Guid?)strangerParent.Id });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a foreign parent is not_found (404), never 422 — no enumeration oracle");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Nesting_parent_that_is_itself_a_child_is_rejected_422()
    {
        // R3 failure shape 1: the chosen parent is itself a child (it has a non-null parentId) → setting it
        // as parent would create a grandchild. This is a 422 (owned-but-illegal), AFTER the 404 ownership check.
        var owner = await CreateOwnerAsync("google-sub-cp-grandchild", "cpgrandchild@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var top = await PutProjectAsync(token, Guid.CreateVersion7(), "Top", "blue", "folder");
        var middle = await PutProjectAsync(token, Guid.CreateVersion7(), "Middle", "green", "star", parentId: top.Id);

        using var response = await SendAsync(
            HttpMethod.Put, ProjectPath(Guid.CreateVersion7()), token,
            new { name = "Grandchild", color = "red", icon = "flag", parentId = (Guid?)middle.Id });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "nesting under a child would create a grandchild (one-level rule, FR-012)");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");
        problem.Errors!.Keys.Should().Contain(k => k.Equals("parentId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Deny_replay_of_an_id_owned_by_another_user_is_404_not_found()
    {
        // R2/R13 DENY: a PUT/replay of an id owned by a DIFFERENT user is 404 (NOT 403, NOT an idempotent hit).
        var owner = await CreateOwnerAsync("google-sub-cp-deny-owner", "cpdenyowner@example.com");
        var stranger = await CreateOwnerAsync("google-sub-cp-deny-stranger", "cpdenystranger@example.com");
        var id = Guid.CreateVersion7();

        await PutProjectAsync(TestJwtHelper.Valid(owner.Value.ToString()), id, "Owner's project", "blue", "folder");

        using var response = await SendAsync(
            HttpMethod.Put, ProjectPath(id), TestJwtHelper.Valid(stranger.Value.ToString()),
            new { name = "Hijack", color = "red", icon = "star", parentId = (Guid?)null });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a foreign id is not_found (404), never 403 — no enumeration oracle");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("not_found");

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Projects.SingleAsync(p => p.Id == ProjectId.From(id));
        stored.OwnerId.Should().Be(owner, "the foreign PUT never reassigned ownership");
        stored.Name.Should().Be("Owner's project", "the foreign PUT never overwrote the owner's row");
    }

    [Fact]
    public async Task Deny_no_jwt_is_rejected_401_with_our_envelope()
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(ProjectPath(Guid.CreateVersion7()), UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { name = "No auth", color = "blue", icon = "folder", parentId = (Guid?)null }),
        };
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "createProject is deny-by-default (FR-068)");
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("unauthenticated");
        problem.Status.Should().Be(401);
    }
}
