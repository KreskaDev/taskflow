using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement.Events;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;
using Wolverine;
using Wolverine.Tracking;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T045, US8) for <c>DELETE /api/tasks/{id}</c> (deleteTask) and the
/// server-authoritative soft-delete reaper (<c>ReapDeletedTask</c>, R8). Soft-delete is
/// version-free and idempotent: it stamps <c>deleted_at</c> (excluding the row from every
/// owner-scoped read, FR-097) and returns 204; a re-delete of the caller's OWN already-tombstoned
/// row is the idempotent no-op 204 (NOT 404 — the 404 posture applies only to foreign/absent ids,
/// research.md R9/R17). A scheduled <c>ReapDeletedTask</c> hard-deletes the still-tombstoned row
/// after the window; it is RESTORE-AWARE and must NOT erase a row whose <c>deleted_at</c> was
/// cleared underneath it.
/// </summary>
/// <remarks>
/// RED-FIRST (Constitution VIII): this file compiles and FAILS until T049 (DeleteTask command +
/// handler + scheduled reaper publish), T050 (<c>ReapDeletedTaskHandler</c>) and T051 (the DELETE
/// route) land.
/// <para>
/// The DELETE endpoint does not exist yet, so the allow / idempotent cases send a DELETE and expect
/// <c>204</c> but observe <c>404</c> (no route) — a clean runtime assertion failure, not a compile
/// error. The reaper cases seed the soft-deleted row DIRECTLY through <see cref="AppDbContext"/>
/// (the DELETE handler is unbuilt this far into TDD) and drive the reaper by invoking the
/// <c>ReapDeletedTask</c> message on the bus, which currently has NO registered handler (T050) — so
/// the in-process invoke throws and the hard-delete assertion fails. We invoke the message directly
/// (with the row's scheduled <c>deleted_at</c> instant) rather than waiting out the 30-second
/// scheduled delay, mirroring how <see cref="IdentityAccess.DeleteAccountTests"/> exercises the
/// durable-queue side of account deletion through the in-process pipeline.
/// </para>
/// </remarks>
public sealed class DeleteTaskTests : IntegrationTestBase
{
    private const string TasksPath = "/api/tasks";
    private const string EnsurePath = "/api/users/ensure";

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

    /// <summary>
    /// Seeds a task directly through the DbContext (the createTask/deleteTask handlers are not all
    /// reachable here), optionally already soft-deleted. Returns the (id, deletedAt) the reaper
    /// cases assert against — <c>deletedAt</c> is the exact instant the row was tombstoned, which is
    /// the scheduled <see cref="ReapDeletedTask.DeletedAtInstant"/> the restore-aware reaper matches.
    /// </summary>
    private async Task<(Guid Id, DateTime? DeletedAt)> SeedTaskAsync(
        UserId owner, string title, string position, bool softDeleted = false)
    {
        var id = Guid.CreateVersion7();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var task = TaskEntity.Create(TaskId.From(id), owner, title, position, DateTime.UtcNow);
        if (softDeleted)
        {
            task.SoftDelete(DateTime.UtcNow);
        }

        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        return (id, task.DeletedAt);
    }

    private async Task<TaskEntity?> LoadRowAsync(Guid id)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Tombstone-inclusive: assert on the persisted row whether or not it is soft-deleted.
        return await db.Tasks.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == TaskId.From(id));
    }

    /// <summary>
    /// Drives the soft-delete reaper synchronously through the in-process tracking harness (no
    /// 30-second wait): sends the <see cref="ReapDeletedTask"/> to its durable local queue and waits
    /// for the activity to DRAIN before returning, so no envelope leaks to host teardown. The message
    /// is auth-exempt (R8), so it needs no principal. Mirrors how <see cref="IdentityAccess.DeleteAccountTests"/>
    /// exercises the durable-queue side of account deletion. RED: no <c>ReapDeletedTaskHandler</c> is
    /// registered yet (T050), so the reaper has no effect and the hard-delete / restore-skip DB
    /// assertions fail (or the tracked send surfaces the missing handler) — never a host crash.
    /// </summary>
    private async Task ReapAsync(Guid id, DateTime deletedAtInstant)
    {
        var host = Services.GetRequiredService<IHost>();
        await host.TrackActivity().Timeout(TimeSpan.FromSeconds(10)).ExecuteAndWaitAsync(
            ctx => ctx.SendAsync(new ReapDeletedTask(TaskId.From(id), deletedAtInstant)));
    }

    [Fact]
    public async Task Allow_owner_soft_deletes_own_task_204_row_excluded_from_list_but_tombstone_persists()
    {
        var owner = await CreateOwnerAsync("google-sub-del-task-200", "deltask200@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var (id, _) = await SeedTaskAsync(owner, "Doomed", "a0");

        using (var response = await SendAsync(HttpMethod.Delete, TaskPath(id), token))
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent, "an owner soft-deleting their own task returns 204");
        }

        // FR-097: the soft-deleted row is excluded from the owner-scoped list read...
        using (var list = await SendAsync(HttpMethod.Get, TasksPath, token))
        {
            list.StatusCode.Should().Be(HttpStatusCode.OK);
            (await list.ReadTasksAsync()).Should().NotContain(t => t.Id == id, "a soft-deleted task is excluded from GET /api/tasks");
        }

        // ...but the row STILL EXISTS with deleted_at stamped (soft-delete, not hard-delete).
        var stored = await LoadRowAsync(id);
        stored.Should().NotBeNull("soft-delete keeps the row; only the reaper hard-deletes it");
        stored!.DeletedAt.Should().NotBeNull("soft-delete stamps deleted_at on the persisted row");
    }

    [Fact]
    public async Task Deny_deleting_another_users_task_is_404_not_found_and_leaves_it_untouched()
    {
        // research.md R9/R17: a foreign id resolves to 404 (NOT 403) — no enumeration oracle — and the
        // owner's row is NOT soft-deleted by the stranger's attempt.
        var owner = await CreateOwnerAsync("google-sub-del-task-owner", "deltaskowner@example.com");
        var stranger = await CreateOwnerAsync("google-sub-del-task-stranger", "deltaskstranger@example.com");
        var (id, _) = await SeedTaskAsync(owner, "Owner's task", "a0");

        using var response = await SendAsync(HttpMethod.Delete, TaskPath(id), TestJwtHelper.Valid(stranger.Value.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a foreign id is not_found (404), never 403");
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("not_found");
        problem.Status.Should().Be(404);

        var stored = await LoadRowAsync(id);
        stored.Should().NotBeNull();
        stored!.DeletedAt.Should().BeNull("the stranger's foreign DELETE never soft-deleted the owner's row");
    }

    [Fact]
    public async Task Deny_deleting_an_absent_id_is_404_not_found()
    {
        var owner = await CreateOwnerAsync("google-sub-del-task-absent", "deltaskabsent@example.com");
        var absentId = Guid.CreateVersion7();

        using var response = await SendAsync(HttpMethod.Delete, TaskPath(absentId), TestJwtHelper.Valid(owner.Value.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "an id that names no row is not_found (404)");
        response.MediaType().Should().Be("application/problem+json");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Allow_idempotent_second_delete_of_own_tombstone_is_204_no_op_not_404()
    {
        // The sharpest deny/allow distinction (openapi.yaml deleteTask): a re-delete of the caller's
        // OWN already-tombstoned row is the idempotent no-op 204 — NOT a 404. The 404 posture is for
        // foreign/absent ids only; soft-delete is version-free and idempotent.
        var owner = await CreateOwnerAsync("google-sub-del-task-idem", "deltaskidem@example.com");
        var token = TestJwtHelper.Valid(owner.Value.ToString());
        var (id, _) = await SeedTaskAsync(owner, "Already gone", "a0", softDeleted: true);

        var before = await LoadRowAsync(id);
        before.Should().NotBeNull();
        var originalDeletedAt = before!.DeletedAt;
        originalDeletedAt.Should().NotBeNull("the seed is already a tombstone");

        using var response = await SendAsync(HttpMethod.Delete, TaskPath(id), token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "re-deleting the caller's OWN tombstone is the idempotent no-op 204, never 404");

        // The idempotent no-op does NOT re-stamp deleted_at (SoftDelete is a guarded no-op on a tombstone).
        var after = await LoadRowAsync(id);
        after.Should().NotBeNull("the idempotent re-delete is a no-op; the row is not hard-deleted by DELETE");
        after!.DeletedAt.Should().Be(originalDeletedAt, "a no-op re-delete does not re-stamp the existing tombstone");
    }

    [Fact]
    public async Task Reaper_hard_deletes_a_still_tombstoned_row_after_the_window()
    {
        // R8: after the scheduled ReapDeletedTask runs for a row that is STILL tombstoned (deleted_at
        // unchanged from the scheduled instant), the row is HARD-deleted (the physical row is gone).
        var owner = await CreateOwnerAsync("google-sub-del-task-reap", "deltaskreap@example.com");
        var (id, deletedAt) = await SeedTaskAsync(owner, "To be reaped", "a0", softDeleted: true);
        deletedAt.Should().NotBeNull();

        await ReapAsync(id, deletedAt!.Value);

        (await LoadRowAsync(id)).Should().BeNull("the reaper hard-deletes a still-tombstoned row — the physical row is gone");
    }

    [Fact]
    public async Task Reaper_no_ops_on_a_restored_cleared_tombstone()
    {
        // R8 restore-awareness: if deleted_at was CLEARED (the row was restored/re-created) after the
        // reaper was scheduled, the reaper must NOT hard-delete it — a blind erase would lose a live row.
        var owner = await CreateOwnerAsync("google-sub-del-task-restore", "deltaskrestore@example.com");

        // Seed a tombstone, capture the scheduled instant, THEN clear deleted_at (simulating a restore).
        var (id, scheduledInstant) = await SeedTaskAsync(owner, "Restored", "a0", softDeleted: true);
        scheduledInstant.Should().NotBeNull();

        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Tasks.IgnoreQueryFilters().SingleAsync(t => t.Id == TaskId.From(id));
            db.Entry(row).Property(nameof(TaskEntity.DeletedAt)).CurrentValue = null;
            await db.SaveChangesAsync();
        }

        // The reaper fires with the ORIGINAL scheduled instant, but deleted_at is now null.
        await ReapAsync(id, scheduledInstant!.Value);

        var stored = await LoadRowAsync(id);
        stored.Should().NotBeNull("a restored (deleted_at cleared) row must survive the reaper");
        stored!.DeletedAt.Should().BeNull("the reaper no-ops on a cleared tombstone — it never re-stamps or erases a live row");
    }

    [Fact]
    public async Task Deny_no_jwt_is_rejected_401_with_our_envelope()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(TaskPath(Guid.CreateVersion7()), UriKind.Relative));
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "deleteTask is deny-by-default (FR-068)");
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("unauthenticated");
        problem.Status.Should().Be(401);
    }
}
