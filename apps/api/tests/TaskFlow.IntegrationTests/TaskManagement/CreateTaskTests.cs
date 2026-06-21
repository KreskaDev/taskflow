using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;
using DomainTaskStatus = TaskFlow.Domain.TaskManagement.TaskStatus;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T029, US1) for <c>PUT /api/tasks/{id}</c> — the idempotent,
/// insert-if-not-exists createTask endpoint keyed on the client-generated UUIDv7 <c>{id}</c>
/// (FR-001, research.md R2). The carrier <c>sub</c> is the caller's own TaskFlow user id
/// (mirrors <see cref="GetCurrentUserTests"/> / <see cref="AccountDeletionCascadeTests"/>), so
/// ownership is established by admitting a user through <c>POST /api/users/ensure</c> and minting
/// a carrier with the returned <c>profile.Id</c>.
/// </summary>
/// <remarks>
/// RED-FIRST (Constitution VIII): this file compiles and FAILS until T031 (command + handler +
/// validator + <c>TaskResponse</c> DTO) and T033 (the PUT route) land. The PUT endpoint does not
/// exist yet, so the allow/idempotent/round-trip cases expect <c>200</c> but observe <c>404</c>
/// (no route). The spec is deliberately written against a LOCAL <see cref="TaskBody"/> read model
/// (Infrastructure/ApiResponse.cs), never the production DTO, so the failure is a clean runtime
/// assertion rather than a compile error.
/// <para>
/// The sharpest assertion is the idempotent replay (research.md R2 case (b)): a second PUT of the
/// SAME id+owner with a DIFFERENT title must return the row UNCHANGED — proving insert-if-not-exists
/// is NOT a blind upsert/replace. A same-title replay would pass a naive upsert and miss the bug, so
/// the replay deliberately changes the title and asserts the STORED title is the original and the
/// <c>version</c> did NOT bump (a fresh <c>Create</c> leaves <c>version = 0</c>; a replace would bump it).
/// </para>
/// </remarks>
public sealed class CreateTaskTests : IntegrationTestBase
{
    private const string EnsurePath = "/api/users/ensure";

    // A representative client-computed fractional rank (digits + lowercase, fractional-indexing
    // alphabet — see research.md R5). The newest-first seed is between(null, head); "a0" stands in.
    private const string ValidRank = "a0";

    private static string TaskPath(Guid id) => $"/api/tasks/{id}";

    /// <summary>Admits a user via the slice-001 ensure path; the returned id is the task-owner identity.</summary>
    private async Task<UserId> CreateOwnerAsync(string sub, string email)
    {
        var profile = await (await SendAsync(
            HttpMethod.Post, EnsurePath, TestJwtHelper.Valid(sub),
            new { googleSubjectId = sub, email, displayName = "Task Owner", avatarUrl = (string?)null }))
            .ReadProfileAsync();
        return UserId.From(profile.Id);
    }

    [Fact]
    public async Task Allow_creates_a_new_task_with_the_client_minted_id_and_default_fields()
    {
        var owner = await CreateOwnerAsync("google-sub-create-200", "create200@example.com");
        var id = Guid.CreateVersion7();

        using var response = await SendAsync(
            HttpMethod.Put, TaskPath(id), TestJwtHelper.Valid(owner.Value.ToString()),
            new { title = "Buy milk", position = ValidRank });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The response body round-trips the lean read model with the server-stamped defaults.
        var body = await response.ReadTaskAsync();
        body.Id.Should().Be(id, "the client-minted UUIDv7 id is the id stored and returned");
        body.Title.Should().Be("Buy milk");
        body.Position.Should().Be(ValidRank);
        body.Status.Should().Be("backlog", "a fresh task defaults to backlog (FR-003)");
        body.Version.Should().Be(0, "a fresh Create starts version at 0 (no mutating behavior ran)");
        body.CompletedAt.Should().BeNull("an uncompleted backlog task has no completion timestamp");

        // The stored row carries the same fields (assert off the DB, typed status, not just the body).
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Tasks.SingleAsync(t => t.Id == TaskId.From(id));
        stored.CreatedBy.Should().Be(owner, "createdBy is the authenticated caller (FR-002)");
        stored.Title.Should().Be("Buy milk");
        stored.Position.Should().Be(ValidRank);
        stored.Status.Should().Be(DomainTaskStatus.Backlog);
        stored.Version.Should().Be(0);
        stored.CompletedAt.Should().BeNull();
        stored.DeletedAt.Should().BeNull("a freshly created task is not soft-deleted");
        stored.CreatedAt.Should().NotBe(default);
        stored.UpdatedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task Allow_the_client_uuidv7_id_round_trips_unchanged()
    {
        var owner = await CreateOwnerAsync("google-sub-create-roundtrip", "roundtrip@example.com");
        var id = Guid.CreateVersion7();

        using var response = await SendAsync(
            HttpMethod.Put, TaskPath(id), TestJwtHelper.Valid(owner.Value.ToString()),
            new { title = "Round trip", position = ValidRank });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadTaskAsync()).Id.Should().Be(id, "the id the caller PUT in the route is the id stored");

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Tasks.AnyAsync(t => t.Id == TaskId.From(id)))
            .Should().BeTrue("the row is keyed on the exact client-supplied id (ValueGeneratedNever)");
    }

    [Fact]
    public async Task Allow_idempotent_replay_returns_the_existing_row_unchanged_and_does_not_bump_version()
    {
        // research.md R2 case (b): a same-id+same-owner retry is an idempotent replay, NOT a replace.
        var owner = await CreateOwnerAsync("google-sub-create-replay", "replay@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var id = Guid.CreateVersion7();

        using (var first = await SendAsync(
            HttpMethod.Put, TaskPath(id), token, new { title = "Original title", position = ValidRank }))
        {
            first.StatusCode.Should().Be(HttpStatusCode.OK);
            (await first.ReadTaskAsync()).Version.Should().Be(0);
        }

        // Replay the SAME id with a DIFFERENT title (a same-title replay would pass a blind upsert
        // and miss the bug — the title change is what proves create is insert-if-not-exists, not replace).
        using var replay = await SendAsync(
            HttpMethod.Put, TaskPath(id), token, new { title = "DIFFERENT title", position = "z9" });

        replay.StatusCode.Should().Be(HttpStatusCode.OK, "a same-owner retry is a SUCCESS (idempotent), never a 409/422");
        var replayed = await replay.ReadTaskAsync();
        replayed.Title.Should().Be("Original title", "the replay returns the existing row UNCHANGED — create does not replace");
        replayed.Position.Should().Be(ValidRank, "the original position is preserved, not overwritten by the replay payload");
        replayed.Version.Should().Be(0, "an idempotent replay does NOT bump the optimistic-concurrency token");

        // The STORED row is likewise unchanged (the catastrophic assertion: a replace would corrupt it).
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Tasks.SingleAsync(t => t.Id == TaskId.From(id));
        stored.Title.Should().Be("Original title", "insert-if-not-exists never replaces the stored title on replay");
        stored.Position.Should().Be(ValidRank);
        stored.Version.Should().Be(0);
    }

    [Fact]
    public async Task ForeignId_PUT_to_an_id_owned_by_another_user_is_rejected_404_not_found()
    {
        // research.md R2 case (c): a PUT/replay of an id owned by a DIFFERENT user is 404 not_found
        // (NOT 403, NOT an idempotent hit) so the id space is not an enumeration oracle.
        var owner = await CreateOwnerAsync("google-sub-create-owner", "owner@example.com");
        var stranger = await CreateOwnerAsync("google-sub-create-stranger", "stranger@example.com");
        var id = Guid.CreateVersion7();

        using (var created = await SendAsync(
            HttpMethod.Put, TaskPath(id), TestJwtHelper.Valid(owner.Value.ToString()),
            new { title = "Owner's task", position = ValidRank }))
        {
            created.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // The stranger replays the SAME id — they don't own it, so it resolves to 404 for them.
        using var response = await SendAsync(
            HttpMethod.Put, TaskPath(id), TestJwtHelper.Valid(stranger.Value.ToString()),
            new { title = "Stranger's hijack", position = "b1" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a foreign id is not_found (404), never 403 — no enumeration oracle");
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("not_found");
        problem.Status.Should().Be(404);

        // The owner's row is untouched by the stranger's hijack attempt.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Tasks.SingleAsync(t => t.Id == TaskId.From(id));
        stored.CreatedBy.Should().Be(owner, "the foreign PUT never reassigned ownership");
        stored.Title.Should().Be("Owner's task", "the foreign PUT never overwrote the owner's title");
    }

    [Fact]
    public async Task Validation_empty_title_is_rejected_422_with_a_field_error()
    {
        var owner = await CreateOwnerAsync("google-sub-create-empty", "empty@example.com");

        using var response = await SendAsync(
            HttpMethod.Put, TaskPath(Guid.CreateVersion7()), TestJwtHelper.Valid(owner.Value.ToString()),
            new { title = "", position = ValidRank });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");
        problem.Status.Should().Be(422);
        problem.Errors.Should().NotBeNull("a validation failure carries field-level messages");
        // Keyed by FIELD (what T029 requires); case-insensitive because the wire key derives from the
        // validator's PropertyName (PascalCase by FluentValidation default) and is serialized as-is —
        // asserting "title" exactly would force a casing fight T031 hasn't settled yet.
        problem.Errors!.Keys.Should().Contain(k => k.Equals("title", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validation_whitespace_only_title_is_rejected_422()
    {
        var owner = await CreateOwnerAsync("google-sub-create-ws", "ws@example.com");

        using var response = await SendAsync(
            HttpMethod.Put, TaskPath(Guid.CreateVersion7()), TestJwtHelper.Valid(owner.Value.ToString()),
            new { title = "   ", position = ValidRank });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "title is non-empty AFTER trimming (FR-001)");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");
        problem.Errors!.Keys.Should().Contain(k => k.Equals("title", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validation_title_over_500_chars_is_rejected_422()
    {
        var owner = await CreateOwnerAsync("google-sub-create-long", "long@example.com");

        // Exactly 501 chars: validation must reject BEFORE the domain NormalizeTitle throws — so this
        // is a 422, never a 500.
        using var response = await SendAsync(
            HttpMethod.Put, TaskPath(Guid.CreateVersion7()), TestJwtHelper.Valid(owner.Value.ToString()),
            new { title = new string('a', 501), position = ValidRank });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a >500 char title fails validation, not the domain guard");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");
        problem.Errors!.Keys.Should().Contain(k => k.Equals("title", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validation_empty_position_rank_is_rejected_422()
    {
        var owner = await CreateOwnerAsync("google-sub-create-emptypos", "emptypos@example.com");

        using var response = await SendAsync(
            HttpMethod.Put, TaskPath(Guid.CreateVersion7()), TestJwtHelper.Valid(owner.Value.ToString()),
            new { title = "Has title", position = "" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "position is required + non-empty (CreateTaskRequest minLength 1)");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");
        problem.Errors!.Keys.Should().Contain(k => k.Equals("position", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validation_malformed_position_rank_is_rejected_422()
    {
        var owner = await CreateOwnerAsync("google-sub-create-badpos", "badpos@example.com");

        // A rank containing a space is out-of-alphabet under any fractional-indexing reading (R5):
        // the server is a format-VALIDATOR, so a malformed rank is a field-level 422, never a 500.
        using var response = await SendAsync(
            HttpMethod.Put, TaskPath(Guid.CreateVersion7()), TestJwtHelper.Valid(owner.Value.ToString()),
            new { title = "Has title", position = "a b" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a malformed rank string is rejected by the format validator");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");
        problem.Errors!.Keys.Should().Contain(k => k.Equals("position", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Deny_no_jwt_is_rejected_401_with_our_envelope()
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(TaskPath(Guid.CreateVersion7()), UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { title = "No auth", position = ValidRank }),
        };
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "createTask is deny-by-default (FR-068)");
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("unauthenticated");
        problem.Status.Should().Be(401);
    }
}
