using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskFlow.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Tracking;
using CommentId = TaskFlow.Domain.TaskManagement.CommentId;
using ReapDeletedComment = TaskFlow.Domain.TaskManagement.Events.ReapDeletedComment;

namespace TaskFlow.IntegrationTests.Comments;

/// <summary>
/// Coverage (T023.a, US-14) for the deferred <c>ReapDeletedCommentHandler</c> off the durable
/// <c>comment-reaper</c> local queue (R5, the <c>ReapDeletedTaskHandler</c> mirror MINUS the version
/// backstop — comments are versionless, so the µs instant match is the SOLE guard). Hard-purges the row
/// (and its <c>comment_mentions</c> cascade) ONLY if it is still the exact same tombstone; restore-aware
/// (a cleared or re-stamped <c>deleted_at</c> → no-op) and idempotent (double delivery → one purge, no
/// error). RED-FIRST: fails until T023.b lands.
/// </summary>
public sealed class ReapDeletedCommentHandlerTests : CommentsTestBase
{
    /// <summary>
    /// Drives the comment reaper synchronously through the in-process tracking harness (no 30s wait):
    /// sends <see cref="ReapDeletedComment"/> to its durable local queue and waits for the activity to
    /// drain. The message is authz-exempt (no caller — the Program.cs exclusion), so it needs no principal.
    /// </summary>
    private async Task ReapAsync(Guid commentId, DateTime deletedAtInstant)
    {
        var host = Services.GetRequiredService<IHost>();
        await host.TrackActivity().Timeout(TimeSpan.FromSeconds(10)).ExecuteAndWaitAsync(
            ctx => ctx.SendAsync(new ReapDeletedComment(CommentId.From(commentId), deletedAtInstant)));
    }

    [Fact]
    public async Task Reaper_hard_purges_a_still_tombstoned_comment_and_its_mentions()
    {
        var s = await CreateSharedScenarioAsync("reap-ok");
        var (id, deletedAt) = await SeedCommentAsync(
            s.TaskId, s.Project.Id, s.Editor, "To be reaped", softDeleted: true, s.Viewer);
        deletedAt.Should().NotBeNull();
        (await CountMentionRowsAsync(id)).Should().Be(1, "the seed persisted one mention row");

        await ReapAsync(id, deletedAt!.Value);

        (await LoadCommentRowAsync(id)).Should().BeNull("the reaper hard-purges a still-tombstoned row");
        (await CountMentionRowsAsync(id)).Should().Be(0, "the comment_mentions rows cascade with the purge");
    }

    [Fact]
    public async Task Reaper_no_ops_on_a_cleared_tombstone_the_slice_014_restore_seam()
    {
        var s = await CreateSharedScenarioAsync("reap-restore");
        var (id, scheduledInstant) = await SeedCommentAsync(
            s.TaskId, s.Project.Id, s.Editor, "Restored", softDeleted: true);
        scheduledInstant.Should().NotBeNull();

        // Simulate the slice-014 restore: clear deleted_at underneath the scheduled reaper.
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Comments.SingleAsync(c => c.Id == CommentId.From(id));
            db.Entry(row).Property(nameof(TaskFlow.Domain.TaskManagement.Comment.DeletedAt)).CurrentValue = null;
            await db.SaveChangesAsync();
        }

        await ReapAsync(id, scheduledInstant!.Value);

        var stored = await LoadCommentRowAsync(id);
        stored.Should().NotBeNull("a restored (deleted_at cleared) comment must survive the reaper");
        stored!.DeletedAt.Should().BeNull("the reaper never re-stamps or erases a live row");
    }

    [Fact]
    public async Task Reaper_no_ops_on_a_re_stamped_different_instant()
    {
        var s = await CreateSharedScenarioAsync("reap-restamp");
        var (id, firstInstant) = await SeedCommentAsync(
            s.TaskId, s.Project.Id, s.Editor, "Re-deleted later", softDeleted: true);
        firstInstant.Should().NotBeNull();

        // Simulate restore + re-delete: deleted_at now carries a DIFFERENT (later) instant, so the FIRST
        // reaper message is stale and must not purge (the new tombstone gets its own reaper).
        var laterInstant = firstInstant!.Value.AddSeconds(5);
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Comments.SingleAsync(c => c.Id == CommentId.From(id));
            db.Entry(row).Property(nameof(TaskFlow.Domain.TaskManagement.Comment.DeletedAt)).CurrentValue = laterInstant;
            await db.SaveChangesAsync();
        }

        await ReapAsync(id, firstInstant.Value);

        var stored = await LoadCommentRowAsync(id);
        stored.Should().NotBeNull("a re-stamped tombstone makes the earlier reaper message stale — no purge");
        stored!.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Reaper_is_idempotent_a_double_delivery_purges_once_without_error()
    {
        var s = await CreateSharedScenarioAsync("reap-idem");
        var (id, deletedAt) = await SeedCommentAsync(
            s.TaskId, s.Project.Id, s.Editor, "Purge once", softDeleted: true);
        deletedAt.Should().NotBeNull();

        await ReapAsync(id, deletedAt!.Value);
        (await LoadCommentRowAsync(id)).Should().BeNull("the first delivery purges the row");

        // The second delivery finds no row and must no-op cleanly (no unhandled exception, no dead-letter).
        await ReapAsync(id, deletedAt.Value);
        (await LoadCommentRowAsync(id)).Should().BeNull();
    }
}
