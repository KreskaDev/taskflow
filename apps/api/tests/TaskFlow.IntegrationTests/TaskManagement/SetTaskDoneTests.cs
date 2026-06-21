using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;
using DomainTaskStatus = TaskFlow.Domain.TaskManagement.TaskStatus;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T043, US1) for <c>PATCH /api/tasks/{id}/status</c> — the
/// desired-state toggle-done command (operationId <c>setTaskDone</c>, contracts/openapi.yaml).
/// The body is <c>{ status: "done"|"backlog", version }</c>: it carries the DESIRED status (not a
/// blind server-side flip, research.md R3) so the write is idempotent under SC-003 optimistic
/// retry, and the caller's last-seen optimistic-concurrency <c>version</c> (research.md R4) so a
/// stale write is rejected with <c>409 version_conflict</c>.
/// </summary>
/// <remarks>
/// RED-FIRST (Constitution VIII): this file compiles and FAILS until the setTaskDone command +
/// handler and the PATCH route land. The endpoint does not exist yet, so a missing route returns
/// 404 for every request: the allow cases (expect 200), the backlog-clears-completedAt case
/// (expect 200), and the stale-version case (expect 409) all fail cleanly (observe 404). The
/// foreign-id case already expects 404 and the no-JWT case already expects 401 (enforced by the
/// auth middleware ahead of routing), so those may pass under RED — the suite still FAILS overall,
/// which is the RED signal. Tasks are seeded DIRECTLY through <see cref="AppDbContext"/> (a
/// <c>done</c> seed is unreachable through the createTask endpoint, which only ever inserts a fresh
/// backlog row), so the seed is the only way to stand up a completed row to un-complete.
/// <para>
/// The sharpest assertion is the idempotent replay (research.md R3): <see cref="MarkDone"/> is
/// unconditional, so a SECOND <c>done</c> (carrying the REFRESHED version) re-stamps
/// <c>completedAt</c> and bumps <c>version</c> again — it does NOT no-op. Idempotency here means the
/// observable DESIRED state is stable: status stays <c>done</c> and <c>completedAt</c> stays
/// non-null across repeated done requests; it must NOT toggle back to backlog. We therefore assert
/// status-stays-done + completedAt-non-null, and deliberately do NOT assert version-unchanged or
/// completedAt-equal (either would force a no-op-if-already-done handler that contradicts R3).
/// </para>
/// </remarks>
public sealed class SetTaskDoneTests : IntegrationTestBase
{
    private const string EnsurePath = "/api/users/ensure";

    // A representative client-computed fractional rank (fractional-indexing alphabet — research.md R5).
    private const string ValidRank = "a0";

    private static string StatusPath(Guid id) => $"/api/tasks/{id}/status";

    /// <summary>Admits a user via the slice-001 ensure path; the returned id is the task-owner identity.</summary>
    private async Task<UserId> CreateOwnerAsync(string sub, string email)
    {
        var profile = await (await SendAsync(
            HttpMethod.Post, EnsurePath, TestJwtHelper.Valid(sub),
            new { googleSubjectId = sub, email, displayName = "Task Owner", avatarUrl = (string?)null }))
            .ReadProfileAsync();
        return UserId.From(profile.Id);
    }

    /// <summary>
    /// Seeds a task directly through <see cref="AppDbContext"/> and returns its <c>(id, version)</c>.
    /// A fresh <c>Create</c> leaves version 0; an optional <paramref name="done"/> marks it complete
    /// (version 1, completedAt stamped) so the backlog-clear case can start from a completed row.
    /// </summary>
    private async Task<(Guid Id, int Version)> SeedTaskAsync(UserId owner, string title, bool done = false)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var id = Guid.CreateVersion7();
        var task = TaskEntity.Create(TaskId.From(id), owner, title, ValidRank, DateTime.UtcNow);
        if (done)
        {
            task.MarkDone(DateTime.UtcNow);
        }

        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return (id, task.Version);
    }

    private static async Task<TaskEntity> LoadAsync(IServiceProvider services, Guid id)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tasks.SingleAsync(t => t.Id == TaskId.From(id));
    }

    [Fact]
    public async Task Allow_done_sets_status_done_and_stamps_completedAt()
    {
        var owner = await CreateOwnerAsync("google-sub-status-done", "statusdone@example.com");
        var (id, version) = await SeedTaskAsync(owner, "Finish the report");

        using var response = await SendAsync(
            HttpMethod.Patch, StatusPath(id), TestJwtHelper.Valid(owner.Value.ToString()),
            new { status = "done", version });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadTaskAsync();
        body.Id.Should().Be(id);
        body.Status.Should().Be("done", "the desired status was done (FR-003)");
        body.CompletedAt.Should().NotBeNull("done stamps completedAt = now (FR-003/FR-004)");
        body.Version.Should().Be(version + 1, "a mutating write bumps the optimistic-concurrency token");

        // The stored row carries the typed status + a completion timestamp (assert off the DB, not just the body).
        var stored = await LoadAsync(Services, id);
        stored.Status.Should().Be(DomainTaskStatus.Done);
        stored.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Allow_backlog_sets_status_backlog_and_clears_completedAt()
    {
        var owner = await CreateOwnerAsync("google-sub-status-backlog", "statusbacklog@example.com");

        // Start from a COMPLETED row (seeded done, version 1, completedAt set) so un-completing it
        // actually exercises the clear — a fresh backlog seed would have a null completedAt already.
        var (id, version) = await SeedTaskAsync(owner, "Re-open this", done: true);

        using var response = await SendAsync(
            HttpMethod.Patch, StatusPath(id), TestJwtHelper.Valid(owner.Value.ToString()),
            new { status = "backlog", version });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadTaskAsync();
        body.Status.Should().Be("backlog", "the desired status was backlog (FR-003)");
        body.CompletedAt.Should().BeNull("backlog clears completedAt (FR-003/FR-004)");
        body.Version.Should().Be(version + 1, "un-completing is a mutating write and bumps the version");

        var stored = await LoadAsync(Services, id);
        stored.Status.Should().Be(DomainTaskStatus.Backlog);
        stored.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Allow_repeated_done_is_idempotent_under_desired_state_and_does_not_toggle_back()
    {
        // research.md R3: the command carries the DESIRED state, not a blind flip — so two done
        // requests (each with the REFRESHED version) both succeed and the task stays done. MarkDone
        // is unconditional, so we assert the STABLE desired state (status=done, completedAt non-null),
        // NOT version-unchanged or completedAt-equal (that would force a no-op-if-already-done handler).
        var owner = await CreateOwnerAsync("google-sub-status-idem", "statusidem@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var (id, version) = await SeedTaskAsync(owner, "Idempotent done");

        int refreshedVersion;
        using (var first = await SendAsync(
            HttpMethod.Patch, StatusPath(id), token, new { status = "done", version }))
        {
            first.StatusCode.Should().Be(HttpStatusCode.OK);
            var firstBody = await first.ReadTaskAsync();
            firstBody.Status.Should().Be("done");
            firstBody.CompletedAt.Should().NotBeNull();
            refreshedVersion = firstBody.Version;
        }

        // Replay done with the version the first response handed back (the client's refreshed last-seen token).
        using var second = await SendAsync(
            HttpMethod.Patch, StatusPath(id), token, new { status = "done", version = refreshedVersion });

        second.StatusCode.Should().Be(HttpStatusCode.OK, "a repeated desired-state write succeeds — it is not a conflict");
        var secondBody = await second.ReadTaskAsync();
        secondBody.Status.Should().Be("done", "a second done keeps the task done — it does NOT toggle back to backlog");
        secondBody.CompletedAt.Should().NotBeNull("the task remains completed across repeated done requests");

        var stored = await LoadAsync(Services, id);
        stored.Status.Should().Be(DomainTaskStatus.Done, "the stable desired state is done");
        stored.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Deny_a_task_owned_by_another_user_is_rejected_404_not_found()
    {
        // research.md R9/R17: ownership (FindOwnedAsync) is checked BEFORE the version compare, so a
        // foreign id resolves to 404 not_found (NEVER 403) for any version value — no enumeration oracle.
        var owner = await CreateOwnerAsync("google-sub-status-owner", "statusowner@example.com");
        var stranger = await CreateOwnerAsync("google-sub-status-stranger", "statusstranger@example.com");
        var (id, version) = await SeedTaskAsync(owner, "Owner's task");

        using var response = await SendAsync(
            HttpMethod.Patch, StatusPath(id), TestJwtHelper.Valid(stranger.Value.ToString()),
            new { status = "done", version });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a foreign id is not_found (404), never 403");
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("not_found");
        problem.Status.Should().Be(404);

        // The owner's row is untouched by the stranger's attempt.
        var stored = await LoadAsync(Services, id);
        stored.Status.Should().Be(DomainTaskStatus.Backlog, "the foreign write never mutated the owner's task");
        stored.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Deny_a_stale_version_is_rejected_409_version_conflict()
    {
        // research.md R4: the handler compares row.Version to command.Version; a stale value → 409
        // version_conflict (NOT conflict_lww). The client refetches and reapplies its intent.
        var owner = await CreateOwnerAsync("google-sub-status-stale", "statusstale@example.com");
        var (id, version) = await SeedTaskAsync(owner, "Concurrency-guarded");

        using var response = await SendAsync(
            HttpMethod.Patch, StatusPath(id), TestJwtHelper.Valid(owner.Value.ToString()),
            new { status = "done", version = version + 7 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "a stale version is an optimistic-concurrency rejection");
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("version_conflict", "the stale-version REJECTION is version_conflict, NOT conflict_lww");
        problem.Status.Should().Be(409);

        // The stale write never landed — the row is unchanged.
        var stored = await LoadAsync(Services, id);
        stored.Status.Should().Be(DomainTaskStatus.Backlog, "the stale write was rejected before any mutation");
        stored.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Deny_no_jwt_is_rejected_401_with_our_envelope()
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri(StatusPath(Guid.CreateVersion7()), UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { status = "done", version = 0 }),
        };
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "setTaskDone is deny-by-default (FR-068)");
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("unauthenticated");
        problem.Status.Should().Be(401);
    }
}
