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
/// Allow + deny coverage (T016, US1) for <c>PATCH /api/projects/{id}/archive</c> + <c>/unarchive</c> —
/// archive is a REVERSIBLE state (sets <c>archived_at</c>, hidden from default views, R2/AS-05), unarchive
/// clears it. A parent-with-children archive takes a REQUIRED child disposition (cascade vs orphan-to-top,
/// AS-10). Unarchive restores a child whose parent is still archived/deleted as TOP-LEVEL (R9). Both carry
/// the optimistic <c>version</c>.
/// </summary>
/// <remarks>
/// RED-FIRST (Constitution VIII): this file compiles and FAILS until T017 (ArchiveProject + UnarchiveProject
/// commands/handlers + the two PATCH routes) lands. The routes do not exist yet, so the allow cases expect
/// 200 but observe 404. List-visibility semantics are asserted on the DB (the archived/default list endpoint
/// is T021, deliberately out of this vertical) — <c>archived_at</c> set = hidden-from-default, still-a-live-row.
/// </remarks>
public sealed class ArchiveProjectTests : IntegrationTestBase
{
    private const string EnsurePath = "/api/users/ensure";

    private static string CreatePath(Guid id) => $"/api/projects/{id}";

    private static string ArchivePath(Guid id) => $"/api/projects/{id}/archive";

    private static string UnarchivePath(Guid id) => $"/api/projects/{id}/unarchive";

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

    private async Task<TaskFlow.Domain.TaskManagement.Project?> LoadAsync(Guid id)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == ProjectId.From(id));
    }

    [Fact]
    public async Task Allow_archive_sets_archived_at_and_bumps_version_keeping_the_row_live()
    {
        var owner = await CreateOwnerAsync("google-sub-ap-archive", "aparchive@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var project = await CreateProjectAsync(token, "Work", "blue", "folder");

        using var response = await SendAsync(HttpMethod.Patch, ArchivePath(project.Id), token, new { version = project.Version });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadProjectAsync();
        body.ArchivedAt.Should().NotBeNull("archive stamps archived_at (R2)");
        body.Version.Should().Be(project.Version + 1, "archive bumps the optimistic-concurrency token");

        var stored = await LoadAsync(project.Id);
        stored.Should().NotBeNull("archive is a reversible STATE, not a tombstone — the row stays live");
        stored!.ArchivedAt.Should().NotBeNull();
        stored.DeletedAt.Should().BeNull("archive must NOT soft-delete (R2: archive != delete)");
    }

    [Fact]
    public async Task Allow_unarchive_clears_archived_at_and_bumps_version()
    {
        var owner = await CreateOwnerAsync("google-sub-ap-unarchive", "apunarchive@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var project = await CreateProjectAsync(token, "Work", "blue", "folder");

        ProjectBody archived;
        using (var archive = await SendAsync(HttpMethod.Patch, ArchivePath(project.Id), token, new { version = project.Version }))
        {
            archive.StatusCode.Should().Be(HttpStatusCode.OK);
            archived = await archive.ReadProjectAsync();
        }

        using var response = await SendAsync(HttpMethod.Patch, UnarchivePath(project.Id), token, new { version = archived.Version });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadProjectAsync();
        body.ArchivedAt.Should().BeNull("unarchive clears archived_at (AS-11)");
        body.Version.Should().Be(archived.Version + 1);
    }

    [Fact]
    public async Task Allow_archive_parent_with_children_cascade_archives_the_children()
    {
        // AS-10 cascade: archiving a parent with childDisposition=cascade archives the children too
        // (the whole subtree follows the parent's reversible fate, R5).
        var owner = await CreateOwnerAsync("google-sub-ap-cascade", "apcascade@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var parent = await CreateProjectAsync(token, "Parent", "blue", "folder");
        var child = await CreateProjectAsync(token, "Child", "green", "star", parentId: parent.Id);

        using var response = await SendAsync(
            HttpMethod.Patch, ArchivePath(parent.Id), token, new { version = parent.Version, childDisposition = "cascade" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadAsync(parent.Id))!.ArchivedAt.Should().NotBeNull("the parent is archived");
        var storedChild = await LoadAsync(child.Id);
        storedChild!.ArchivedAt.Should().NotBeNull("childDisposition=cascade archives the children with the parent (AS-10)");
        storedChild.ParentId.Should().Be(ProjectId.From(parent.Id), "a cascaded child keeps its parent link");
    }

    [Fact]
    public async Task Allow_archive_parent_with_children_orphan_to_top_promotes_children_to_top_level()
    {
        // AS-10 orphan_to_top: archiving a parent with childDisposition=orphan_to_top promotes the children
        // to top-level (their parent_id is nulled) and leaves them ACTIVE.
        var owner = await CreateOwnerAsync("google-sub-ap-orphan", "aporphan@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var parent = await CreateProjectAsync(token, "Parent", "blue", "folder");
        var child = await CreateProjectAsync(token, "Child", "green", "star", parentId: parent.Id);

        using var response = await SendAsync(
            HttpMethod.Patch, ArchivePath(parent.Id), token, new { version = parent.Version, childDisposition = "orphan_to_top" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadAsync(parent.Id))!.ArchivedAt.Should().NotBeNull("the parent is archived");
        var storedChild = await LoadAsync(child.Id);
        storedChild!.ParentId.Should().BeNull("childDisposition=orphan_to_top promotes the child to top-level (AS-10)");
        storedChild.ArchivedAt.Should().BeNull("an orphaned child stays ACTIVE, it is not archived");
    }

    [Fact]
    public async Task Validation_archive_parent_with_children_without_child_disposition_is_422()
    {
        // AS-10: when the project has child projects, childDisposition is REQUIRED → omitting it is a 422
        // (a cross-row check in the handler, like the nesting guard, not a stateless validator rule).
        var owner = await CreateOwnerAsync("google-sub-ap-nodisp", "apnodisp@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var parent = await CreateProjectAsync(token, "Parent", "blue", "folder");
        _ = await CreateProjectAsync(token, "Child", "green", "star", parentId: parent.Id);

        using var response = await SendAsync(HttpMethod.Patch, ArchivePath(parent.Id), token, new { version = parent.Version });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a parent-with-children archive REQUIRES a child disposition (AS-10)");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");

        (await LoadAsync(parent.Id))!.ArchivedAt.Should().BeNull("the rejected archive never archived the parent");
    }

    [Fact]
    public async Task Unarchive_child_of_still_archived_parent_is_restored_top_level()
    {
        // R9: unarchiving a child whose parent is STILL archived nulls its parent_id, restoring it as
        // top-level rather than re-nesting it under a hidden parent.
        var owner = await CreateOwnerAsync("google-sub-ap-r9", "apr9@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var parent = await CreateProjectAsync(token, "Parent", "blue", "folder");
        var child = await CreateProjectAsync(token, "Child", "green", "star", parentId: parent.Id);

        // Archive the parent (cascade also archives the child).
        int childVersionAfterCascade;
        using (var archive = await SendAsync(
            HttpMethod.Patch, ArchivePath(parent.Id), token, new { version = parent.Version, childDisposition = "cascade" }))
        {
            archive.StatusCode.Should().Be(HttpStatusCode.OK);
            childVersionAfterCascade = (await LoadAsync(child.Id))!.Version;
        }

        // Unarchive ONLY the child; its parent is still archived → it must be promoted to top-level (R9).
        using var response = await SendAsync(
            HttpMethod.Patch, UnarchivePath(child.Id), token, new { version = childVersionAfterCascade });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadProjectAsync();
        body.ArchivedAt.Should().BeNull("the child is unarchived");
        body.ParentId.Should().BeNull("R9: a child whose parent is still archived is restored TOP-LEVEL, not re-nested under a hidden parent");

        (await LoadAsync(parent.Id))!.ArchivedAt.Should().NotBeNull("the parent stays archived — only the child was unarchived");
    }

    [Fact]
    public async Task Archive_stale_version_is_rejected_409()
    {
        var owner = await CreateOwnerAsync("google-sub-ap-stale", "apstale@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var project = await CreateProjectAsync(token, "Work", "blue", "folder");

        using var response = await SendAsync(HttpMethod.Patch, ArchivePath(project.Id), token, new { version = project.Version + 99 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "a stale version is rejected with 409 version_conflict");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("version_conflict");
    }

    [Fact]
    public async Task Deny_archiving_another_users_project_is_404_not_found()
    {
        var owner = await CreateOwnerAsync("google-sub-ap-deny-owner", "apdenyowner@example.com");
        var stranger = await CreateOwnerAsync("google-sub-ap-deny-stranger", "apdenystranger@example.com");
        var project = await CreateProjectAsync(TestJwtHelper.Valid(owner.Value.ToString()), "Owner's", "blue", "folder");

        using var response = await SendAsync(
            HttpMethod.Patch, ArchivePath(project.Id), TestJwtHelper.Valid(stranger.Value.ToString()), new { version = project.Version });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a foreign id is not_found (404), never 403");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");

        (await LoadAsync(project.Id))!.ArchivedAt.Should().BeNull("the foreign archive never touched the owner's row");
    }

    [Fact]
    public async Task Deny_unarchiving_another_users_project_is_404_not_found()
    {
        var owner = await CreateOwnerAsync("google-sub-ap-deny-un-owner", "apdenyunowner@example.com");
        var stranger = await CreateOwnerAsync("google-sub-ap-deny-un-stranger", "apdenyunstranger@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var project = await CreateProjectAsync(token, "Owner's", "blue", "folder");
        int archivedVersion;
        using (var archive = await SendAsync(HttpMethod.Patch, ArchivePath(project.Id), token, new { version = project.Version }))
        {
            archive.StatusCode.Should().Be(HttpStatusCode.OK);
            archivedVersion = (await archive.ReadProjectAsync()).Version;
        }

        using var response = await SendAsync(
            HttpMethod.Patch, UnarchivePath(project.Id), TestJwtHelper.Valid(stranger.Value.ToString()), new { version = archivedVersion });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a foreign id is not_found (404), never 403");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");

        (await LoadAsync(project.Id))!.ArchivedAt.Should().NotBeNull("the foreign unarchive never restored the owner's archived row");
    }

    [Fact]
    public async Task Deny_no_jwt_is_rejected_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri(ArchivePath(Guid.CreateVersion7()), UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { version = 0 }),
        };
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "archiveProject is deny-by-default (FR-068)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
    }
}
