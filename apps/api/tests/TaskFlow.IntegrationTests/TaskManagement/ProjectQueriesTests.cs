using System.Net;
using FluentAssertions;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T020, US1) for <c>GET /api/projects?archived=</c> (getMyProjects) — the
/// owner-scoped, tombstone-excluded project list (R8). Two DISJOINT view sets: <c>archived=false</c>
/// (default) returns ACTIVE projects for the sidebar (AS-05: archived hidden); <c>archived=true</c>
/// returns the caller's archived projects so unarchive (AS-11) is reachable. Flat list; the one-level tree
/// is assembled client-side (R16).
/// </summary>
/// <remarks>
/// RED-FIRST (Constitution VIII): this file compiles and FAILS until T021 (GetMyProjects query + handler +
/// the GET route) lands. The route does not exist yet, so the allow cases expect 200 but observe 404.
/// </remarks>
public sealed class ProjectQueriesTests : IntegrationTestBase
{
    private const string EnsurePath = "/api/users/ensure";
    private const string ListPath = "/api/projects";
    private const string ArchivedListPath = "/api/projects?archived=true";

    private static string CreatePath(Guid id) => $"/api/projects/{id}";

    private static string ArchivePath(Guid id) => $"/api/projects/{id}/archive";

    private static string DeletePath(Guid id, int version) => $"/api/projects/{id}?version={version}";

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
    public async Task Allow_default_list_returns_active_projects_excluding_archived_and_deleted()
    {
        var owner = await CreateOwnerAsync("google-sub-pq-active", "pqactive@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var active = await CreateProjectAsync(token, "Active", "blue", "folder");
        var toArchive = await CreateProjectAsync(token, "ToArchive", "green", "star");
        var toDelete = await CreateProjectAsync(token, "ToDelete", "red", "flag");

        using (var archive = await SendAsync(HttpMethod.Patch, ArchivePath(toArchive.Id), token, new { version = toArchive.Version }))
        {
            archive.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var del = await SendAsync(HttpMethod.Delete, DeletePath(toDelete.Id, toDelete.Version), token))
        {
            del.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var response = await SendAsync(HttpMethod.Get, ListPath, token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var projects = await response.ReadProjectsAsync();
        projects.Should().Contain(p => p.Id == active.Id, "the default list returns ACTIVE projects (AS-05)");
        projects.Should().NotContain(p => p.Id == toArchive.Id, "archived projects are hidden from the default list (AS-05)");
        projects.Should().NotContain(p => p.Id == toDelete.Id, "tombstoned projects are excluded from all queries (R2)");
    }

    [Fact]
    public async Task Allow_archived_list_returns_only_archived_projects()
    {
        var owner = await CreateOwnerAsync("google-sub-pq-archived", "pqarchived@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var active = await CreateProjectAsync(token, "Active", "blue", "folder");
        var archived = await CreateProjectAsync(token, "Archived", "green", "star");

        using (var archive = await SendAsync(HttpMethod.Patch, ArchivePath(archived.Id), token, new { version = archived.Version }))
        {
            archive.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var response = await SendAsync(HttpMethod.Get, ArchivedListPath, token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var projects = await response.ReadProjectsAsync();
        projects.Should().Contain(p => p.Id == archived.Id, "the archived list returns archived projects (AS-11 reachability, R8)");
        projects.Should().NotContain(p => p.Id == active.Id, "the archived list is DISJOINT from the active set (R8)");
        projects.Should().OnlyContain(p => p.ArchivedAt != null, "every row in the archived listing is archived");
    }

    [Fact]
    public async Task Allow_list_is_owner_scoped_never_leaking_another_users_projects()
    {
        var owner = await CreateOwnerAsync("google-sub-pq-scope-owner", "pqscopeowner@example.com");
        var stranger = await CreateOwnerAsync("google-sub-pq-scope-stranger", "pqscopestranger@example.com");
        var mine = await CreateProjectAsync(TestJwtHelper.Valid(owner.Value.ToString()), "Mine", "blue", "folder");
        var theirs = await CreateProjectAsync(TestJwtHelper.Valid(stranger.Value.ToString()), "Theirs", "red", "star");

        using var response = await SendAsync(HttpMethod.Get, ListPath, TestJwtHelper.Valid(owner.Value.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var projects = await response.ReadProjectsAsync();
        projects.Should().Contain(p => p.Id == mine.Id, "the caller sees their own projects");
        projects.Should().NotContain(p => p.Id == theirs.Id, "the list is owner-scoped — never another user's projects (R13)");
    }

    [Fact]
    public async Task Allow_list_never_exposes_owner_id_in_the_read_model()
    {
        // The read-model leak rule (data-model §4): ProjectResponse never carries ownerId/deletedAt. The
        // decoded ProjectBody has no such members, so a leak would have to appear as extra JSON — assert the
        // shape decodes and the projects are present (the absence of ownerId is structural).
        var owner = await CreateOwnerAsync("google-sub-pq-leak", "pqleak@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var project = await CreateProjectAsync(token, "Secret", "blue", "folder");

        using var response = await SendAsync(HttpMethod.Get, ListPath, token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain(project.Id.ToString(), "the caller's project is present in the list");
        raw.Should().NotContain("ownerId", "the read model never exposes ownerId (always the caller)");
        raw.Should().NotContain("deletedAt", "the read model never exposes deletedAt");
    }

    [Fact]
    public async Task Deny_no_jwt_is_rejected_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ListPath, UriKind.Relative));
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "listProjects is deny-by-default (FR-068)");
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("unauthenticated");
        problem.Status.Should().Be(401);
    }
}
