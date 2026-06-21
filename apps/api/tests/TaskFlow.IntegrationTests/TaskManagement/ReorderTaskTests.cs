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
/// Allow + deny coverage (T044, US1) for <c>PATCH /api/tasks/{id}/position</c> — reorderTask, the
/// optimistic-concurrency reorder write (FR-102, research.md R4/R5). Body is
/// <c>{position, version}</c>: the server validates the client-computed lexicographic rank string,
/// is the SOLE writer under the <c>version</c> guard, and never generates ranks.
/// </summary>
/// <remarks>
/// RED-FIRST (Constitution VIII): the <c>PATCH /api/tasks/{id}/position</c> route + its command,
/// handler and validator do NOT exist yet, so every case that drives the reorder verb currently
/// fails — the allow/stale-version/422 cases observe a bare routing <c>404</c> (no route) where they
/// assert <c>200</c>/<c>409</c>/<c>422</c> with a ProblemDetails body. The spec sends ANONYMOUS bodies
/// (never the unbuilt <c>ReorderTaskRequest</c> DTO) and decodes through the local <see cref="TaskBody"/>
/// read model, so the failures are clean runtime assertions rather than compile errors.
/// <para>
/// Tasks are seeded DIRECTLY through <see cref="AppDbContext"/> (createTask is exercised by its own
/// spec). The auth posture (research.md R9/R17) is asserted on the BODY, not just the status code:
/// a foreign id resolves to <c>404 not_found</c> with an <c>application/problem+json</c> envelope — a
/// bare routing 404 carries no problem body, so the body assertions are what make the deny case fail
/// meaningfully now and pass when the handler lands.
/// </para>
/// <para>
/// The equal-rank tie-break case never touches the reorder verb: it seeds two of the caller's tasks
/// at the SAME <c>position</c> and asserts <c>GET /api/tasks</c> returns them ordered by <c>id</c>
/// (research.md R6, <c>ORDER BY position, id</c>). Expected id order is taken from ORDINAL hex
/// comparison — matching Postgres <c>uuid</c> byte order, NOT .NET's default Guid comparer (R6).
/// </para>
/// </remarks>
public sealed class ReorderTaskTests : IntegrationTestBase
{
    private const string TasksPath = "/api/tasks";
    private const string EnsurePath = "/api/users/ensure";

    // A representative client-computed fractional rank the reorder targets (digits + lowercase,
    // fractional-indexing alphabet — research.md R5).
    private const string NewRank = "a5";

    private static string PositionPath(Guid id) => $"/api/tasks/{id}/position";

    private async Task<UserId> CreateAccountAsync(string sub, string email)
    {
        var profile = await (await SendAsync(
            HttpMethod.Post, EnsurePath, TestJwtHelper.Valid(sub),
            new { googleSubjectId = sub, email, displayName = "Reorder Owner", avatarUrl = (string?)null }))
            .ReadProfileAsync();

        return UserId.From(profile.Id);
    }

    /// <summary>Seeds a fresh (version 0) task directly through the DbContext and returns its id.</summary>
    private async Task<Guid> SeedTaskAsync(UserId owner, string title, string position)
    {
        var id = Guid.CreateVersion7();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Tasks.Add(TaskEntity.Create(TaskId.From(id), owner, title, position, DateTime.UtcNow));
        await db.SaveChangesAsync();

        return id;
    }

    /// <summary>Seeds a task at a CALLER-SUPPLIED id (for the deterministic id-ordered tie-break).</summary>
    private async Task SeedTaskWithIdAsync(UserId owner, Guid id, string title, string position)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Tasks.Add(TaskEntity.Create(TaskId.From(id), owner, title, position, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Allow_owner_reorders_own_task_returns_200_with_new_position_and_bumped_version()
    {
        var owner = await CreateAccountAsync("google-sub-reorder-200", "reorder200@example.com");
        var id = await SeedTaskAsync(owner, "Movable", "a0");

        using var response = await SendAsync(
            HttpMethod.Patch, PositionPath(id), TestJwtHelper.Valid(owner.Value.ToString()),
            new { position = NewRank, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadTaskAsync();
        body.Id.Should().Be(id);
        body.Position.Should().Be(NewRank, "the reorder persists the client-computed rank");
        body.Version.Should().Be(1, "a reorder is a mutating write — it bumps the optimistic-concurrency token");

        // The stored row carries the new rank + bumped version (assert off the DB, not just the body).
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Tasks.SingleAsync(t => t.Id == TaskId.From(id));
        stored.Position.Should().Be(NewRank);
        stored.Version.Should().Be(1);
    }

    [Fact]
    public async Task Deny_reordering_another_users_task_is_rejected_404_not_found()
    {
        // research.md R9/R17: a foreign id is not_found (404), NEVER 403 — no enumeration oracle.
        var owner = await CreateAccountAsync("google-sub-reorder-owner", "reorderowner@example.com");
        var stranger = await CreateAccountAsync("google-sub-reorder-stranger", "reorderstranger@example.com");
        var id = await SeedTaskAsync(owner, "Owner's task", "a0");

        using var response = await SendAsync(
            HttpMethod.Patch, PositionPath(id), TestJwtHelper.Valid(stranger.Value.ToString()),
            new { position = NewRank, version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a foreign id is not_found (404), never 403");
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("not_found");
        problem.Status.Should().Be(404);

        // The owner's row is untouched by the stranger's reorder attempt.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Tasks.SingleAsync(t => t.Id == TaskId.From(id));
        stored.Position.Should().Be("a0", "the foreign reorder never moved the owner's task");
        stored.Version.Should().Be(0);
    }

    [Fact]
    public async Task StaleVersion_reorder_with_a_mismatched_version_is_rejected_409_version_conflict()
    {
        // research.md R4: load row; row.Version != command.Version -> VersionConflictException (409).
        var owner = await CreateAccountAsync("google-sub-reorder-stale", "reorderstale@example.com");
        var id = await SeedTaskAsync(owner, "Has moved underneath me", "a0");

        // The caller's last-seen version is stale (1) while the stored row is still at version 0.
        using var response = await SendAsync(
            HttpMethod.Patch, PositionPath(id), TestJwtHelper.Valid(owner.Value.ToString()),
            new { position = NewRank, version = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "a stale version is rejected, not last-write-wins");
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("version_conflict", "the optimistic-concurrency rejection code, NOT conflict_lww");
        problem.Status.Should().Be(409);

        // The stored row is untouched — the stale write was rejected before it could move the row.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Tasks.SingleAsync(t => t.Id == TaskId.From(id));
        stored.Position.Should().Be("a0", "a rejected stale reorder leaves the position unchanged");
        stored.Version.Should().Be(0, "a rejected stale reorder does not bump the version");
    }

    [Fact]
    public async Task Validation_empty_position_rank_is_rejected_422()
    {
        var owner = await CreateAccountAsync("google-sub-reorder-emptypos", "reorderemptypos@example.com");
        var id = await SeedTaskAsync(owner, "Has title", "a0");

        // A valid version is supplied so the only fault is the empty rank (ReorderTaskRequest minLength 1).
        using var response = await SendAsync(
            HttpMethod.Patch, PositionPath(id), TestJwtHelper.Valid(owner.Value.ToString()),
            new { position = "", version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "position is required + non-empty");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");
        problem.Errors!.Keys.Should().Contain(k => k.Equals("position", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validation_malformed_position_rank_is_rejected_422()
    {
        var owner = await CreateAccountAsync("google-sub-reorder-badpos", "reorderbadpos@example.com");
        var id = await SeedTaskAsync(owner, "Has title", "a0");

        // A rank containing a space is out-of-alphabet under any fractional-indexing reading (R5):
        // the server is a format-VALIDATOR, so a malformed rank is a field-level 422, never a 500.
        using var response = await SendAsync(
            HttpMethod.Patch, PositionPath(id), TestJwtHelper.Valid(owner.Value.ToString()),
            new { position = "a b", version = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a malformed rank string is rejected by the format validator");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");
        problem.Errors!.Keys.Should().Contain(k => k.Equals("position", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TieBreak_two_tasks_with_the_same_position_are_returned_ordered_by_id()
    {
        // research.md R6: ORDER BY position, id where id is the time-ordered UUIDv7. The tie-break is
        // deterministic and reproduced identically by client and Postgres because canonical-hex UUIDv7
        // string order matches the uuid column's byte order — so expected order is taken from ORDINAL
        // hex comparison, NOT .NET's default Guid comparer (which would not match Postgres).
        var caller = await CreateAccountAsync("google-sub-reorder-tie", "reordertie@example.com");

        var idA = Guid.CreateVersion7();
        var idB = Guid.CreateVersion7();
        var (firstId, secondId) =
            string.CompareOrdinal(idA.ToString(), idB.ToString()) < 0 ? (idA, idB) : (idB, idA);

        // Seed BOTH at the SAME position, and seed the second-by-id FIRST so a pass under insertion
        // order would invert the expected result — the assertion exercises the server's id tie-break.
        await SeedTaskWithIdAsync(caller, secondId, "Seeded second", NewRank);
        await SeedTaskWithIdAsync(caller, firstId, "Seeded first", NewRank);

        using var response = await SendAsync(HttpMethod.Get, TasksPath, TestJwtHelper.Valid(caller.Value.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tasks = await response.ReadTasksAsync();

        tasks.Should().HaveCount(2);
        tasks.Should().OnlyContain(t => t.Position == NewRank, "both rows share the same rank — the id breaks the tie");

        // Two equal-rank tasks are ordered by ascending id (ORDER BY position, id).
        tasks.Select(t => t.Id).Should().ContainInOrder(firstId, secondId);
    }

    [Fact]
    public async Task Deny_no_jwt_is_rejected_401_with_our_envelope()
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri(PositionPath(Guid.CreateVersion7()), UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { position = NewRank, version = 0 }),
        };
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "reorderTask is deny-by-default (FR-068)");
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("unauthenticated");
        problem.Status.Should().Be(401);
    }
}
