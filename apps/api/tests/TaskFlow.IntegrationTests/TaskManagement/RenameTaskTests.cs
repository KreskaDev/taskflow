using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T042, US2) for <c>PATCH /api/tasks/{id}/title</c> (renameTask) — the
/// optimistic-concurrency rename endpoint. The request carries the new <c>title</c> plus the caller's
/// last-seen <c>version</c> (RenameTaskRequest); the handler loads the caller's NON-deleted row
/// (owner-scoped, research.md R9/R17), 404s a foreign/absent/tombstoned id (NOT 403), rejects a stale
/// <c>version</c> with 409 <c>version_conflict</c> (research.md R4), and on success calls
/// <c>Task.Rename</c> (which bumps <c>Version</c> + stamps <c>UpdatedAt</c>) → 200.
/// </summary>
/// <remarks>
/// RED-FIRST (Constitution VIII): this file compiles and FAILS until the rename command + handler +
/// validator and the PATCH route land. The endpoint does NOT exist yet, so the allow / stale / 422
/// cases hit no route and fail (a bare framework 404 carries no <c>application/problem+json</c>
/// envelope, so <see cref="ApiResponse.ReadProblemAsync"/> / the status assertions blow up). The deny
/// case deliberately asserts the FULL not_found envelope (media type + <c>not_found</c> code + 404),
/// NOT a bare 404 — so it fails now (no envelope) and genuinely verifies the 404-not-403 posture at
/// GREEN rather than falsely passing on the routeless 404. Only the no-JWT case PASSES under RED (auth
/// runs before routing); the suite still fails overall.
/// <para>
/// Tasks are seeded DIRECTLY through <see cref="AppDbContext"/> (a freshly <c>Create</c>d row is
/// <c>Version == 0</c>) because the create endpoint is irrelevant here — this isolates the RED strictly
/// to the rename endpoint and gives deterministic control over the stored <c>version</c>.
/// </para>
/// </remarks>
public sealed class RenameTaskTests : IntegrationTestBase
{
    private const string EnsurePath = "/api/users/ensure";

    // A representative client-computed fractional rank (fractional-indexing alphabet, research.md R5).
    private const string ValidRank = "a0";

    private static string RenamePath(Guid id) => $"/api/tasks/{id}/title";

    /// <summary>Admits a user via the slice-001 ensure path; the returned id is the task-owner identity.</summary>
    private async Task<UserId> CreateOwnerAsync(string sub, string email)
    {
        var profile = await (await SendAsync(
            HttpMethod.Post, EnsurePath, TestJwtHelper.Valid(sub),
            new { googleSubjectId = sub, email, displayName = "Task Owner", avatarUrl = (string?)null }))
            .ReadProfileAsync();
        return UserId.From(profile.Id);
    }

    /// <summary>Seeds a fresh (version 0) non-deleted task owned by <paramref name="owner"/> and returns its id.</summary>
    private async Task<Guid> SeedTaskAsync(UserId owner, string title)
    {
        var id = Guid.CreateVersion7();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Tasks.Add(TaskEntity.Create(TaskId.From(id), owner, title, ValidRank, DateTime.UtcNow));
        await db.SaveChangesAsync();

        return id;
    }

    [Fact]
    public async Task Allow_owner_renames_own_task_updates_title_and_bumps_version()
    {
        var owner = await CreateOwnerAsync("google-sub-rename-200", "rename200@example.com");
        var id = await SeedTaskAsync(owner, "Original title");

        using var response = await SendAsync(
            HttpMethod.Patch, RenamePath(id), TestJwtHelper.Valid(owner.Value.ToString()),
            new { title = "Renamed title", version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.ReadTaskAsync();
        body.Id.Should().Be(id);
        body.Title.Should().Be("Renamed title", "the rename replaces the stored title (FR-001)");
        body.Version.Should().Be(1, "Task.Rename bumps the optimistic-concurrency token from 0 to 1 (R4)");

        // The stored row carries the renamed title + bumped version (assert off the DB, not just the body).
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Tasks.SingleAsync(t => t.Id == TaskId.From(id));
        stored.Title.Should().Be("Renamed title");
        stored.Version.Should().Be(1);
        stored.CreatedBy.Should().Be(owner, "rename never reassigns ownership");
    }

    [Fact]
    public async Task Deny_another_user_renaming_is_rejected_404_not_found_not_403()
    {
        // research.md R9/R17: a foreign id resolves to 404 (NOT 403) so the id space is not an
        // enumeration oracle. Ownership is checked BEFORE the version, so the version value is irrelevant.
        var owner = await CreateOwnerAsync("google-sub-rename-owner", "renameowner@example.com");
        var stranger = await CreateOwnerAsync("google-sub-rename-stranger", "renamestranger@example.com");
        var id = await SeedTaskAsync(owner, "Owner's task");

        using var response = await SendAsync(
            HttpMethod.Patch, RenamePath(id), TestJwtHelper.Valid(stranger.Value.ToString()),
            new { title = "Stranger's hijack", version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a foreign id is not_found (404), never 403 — no enumeration oracle");
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("not_found");
        problem.Status.Should().Be(404);

        // The owner's row is untouched by the stranger's hijack attempt.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Tasks.SingleAsync(t => t.Id == TaskId.From(id));
        stored.Title.Should().Be("Owner's task", "the foreign rename never overwrote the owner's title");
        stored.Version.Should().Be(0, "the foreign rename never bumped the owner's version");
    }

    [Fact]
    public async Task StaleVersion_a_wrong_version_is_rejected_409_version_conflict()
    {
        // research.md R4: the handler loads the row and compares versions; row.Version (0) != the
        // caller's stale version (7) → VersionConflictException → 409 version_conflict.
        var owner = await CreateOwnerAsync("google-sub-rename-stale", "renamestale@example.com");
        var id = await SeedTaskAsync(owner, "Original title");

        using var response = await SendAsync(
            HttpMethod.Patch, RenamePath(id), TestJwtHelper.Valid(owner.Value.ToString()),
            new { title = "Renamed title", version = 7 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "a stale version is rejected (optimistic concurrency, R4)");
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("version_conflict", "the stale-version REJECTION is version_conflict, never conflict_lww (R4)");
        problem.Status.Should().Be(409);

        // The stale rename never mutated the stored row.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Tasks.SingleAsync(t => t.Id == TaskId.From(id));
        stored.Title.Should().Be("Original title", "a version conflict leaves the stored title unchanged");
        stored.Version.Should().Be(0, "a rejected rename never bumps the version");
    }

    [Fact]
    public async Task Validation_empty_title_is_rejected_422_with_a_field_error()
    {
        var owner = await CreateOwnerAsync("google-sub-rename-empty", "renameempty@example.com");
        var id = await SeedTaskAsync(owner, "Original title");

        using var response = await SendAsync(
            HttpMethod.Patch, RenamePath(id), TestJwtHelper.Valid(owner.Value.ToString()),
            new { title = "", version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");
        problem.Status.Should().Be(422);
        problem.Errors.Should().NotBeNull("a validation failure carries field-level messages");
        problem.Errors!.Keys.Should().Contain(k => k.Equals("title", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validation_whitespace_only_title_is_rejected_422()
    {
        var owner = await CreateOwnerAsync("google-sub-rename-ws", "renamews@example.com");
        var id = await SeedTaskAsync(owner, "Original title");

        using var response = await SendAsync(
            HttpMethod.Patch, RenamePath(id), TestJwtHelper.Valid(owner.Value.ToString()),
            new { title = "   ", version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "title is non-empty AFTER trimming (FR-001)");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");
        problem.Errors!.Keys.Should().Contain(k => k.Equals("title", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validation_title_over_500_chars_is_rejected_422()
    {
        var owner = await CreateOwnerAsync("google-sub-rename-long", "renamelong@example.com");
        var id = await SeedTaskAsync(owner, "Original title");

        // Exactly 501 chars: validation must reject BEFORE the domain NormalizeTitle throws — so this
        // is a 422, never a 500.
        using var response = await SendAsync(
            HttpMethod.Patch, RenamePath(id), TestJwtHelper.Valid(owner.Value.ToString()),
            new { title = new string('a', 501), version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a >500 char title fails validation, not the domain guard");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");
        problem.Errors!.Keys.Should().Contain(k => k.Equals("title", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Deny_no_jwt_is_rejected_401_with_our_envelope()
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri(RenamePath(Guid.CreateVersion7()), UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { title = "No auth", version = 0 }),
        };
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "renameTask is deny-by-default (FR-068)");
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("unauthenticated");
        problem.Status.Should().Be(401);
    }
}
