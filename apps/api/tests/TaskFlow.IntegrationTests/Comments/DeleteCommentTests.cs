using System.Net;
using FluentAssertions;
using TaskFlow.IntegrationTests.Infrastructure;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.Comments;

/// <summary>
/// Allow + deny coverage (T022, US-14) for <c>DELETE /api/comments/{commentId}</c> (operationId
/// <c>deleteComment</c>, slice 009). Author-only under the STRICT two-step gate (R4); the delete is a
/// SOFT-delete (stamps <c>deleted_at</c>, the comment leaves the thread — AS-04) that schedules exactly ONE
/// <c>ReapDeletedComment</c> (+30s, the Constitution-VII undo window, R5); version-free/IDEMPOTENT (an own
/// already-deleted replay → 204 no-op, no second reaper); NO 409 (versionless, R2). RED-FIRST: fails until
/// T023 lands.
/// </summary>
/// <remarks>
/// The scheduled-reaper assertion queries Wolverine's DURABLE scheduled storage (the DeleteTaskTests
/// mechanism) rather than <c>tracked.Sent.MessagesOf&lt;ReapDeletedComment&gt;()</c>: a +30s ScheduleDelay
/// parks the publish as a 'Scheduled' incoming envelope, which an in-process tracking session cannot capture
/// without waiting out the delay.
/// </remarks>
public sealed class DeleteCommentTests : CommentsTestBase
{
    [Fact]
    public async Task Allow_author_delete_is_204_soft_delete_leaves_thread_and_schedules_one_reaper()
    {
        var s = await CreateSharedScenarioAsync("del-ok");
        var posted = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Doomed comment");
        var sibling = await PostCommentAsync(TokenFor(s.Owner), s.TaskId, "Surviving sibling");

        using (var response = await SendAsync(HttpMethod.Delete, CommentPath(posted.Id), TokenFor(s.Editor)))
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent, "the author soft-deletes their own comment");
        }

        // SOFT-delete: the row persists with deleted_at stamped (the 30s undo substrate, R5)...
        var stored = await LoadCommentRowAsync(posted.Id);
        stored.Should().NotBeNull("soft-delete keeps the row; only the reaper hard-purges it");
        stored!.DeletedAt.Should().NotBeNull("deleted_at is the soft-delete tombstone");

        // ...while the comment leaves the THREAD immediately (AS-04 — the deleted_at IS NULL filter).
        using (var list = await SendAsync(HttpMethod.Get, CommentsPath(s.TaskId), TokenFor(s.Owner)))
        {
            list.StatusCode.Should().Be(HttpStatusCode.OK);
            var thread = await list.ReadCommentListAsync();
            thread.Comments.Should().NotContain(c => c.Id == posted.Id, "a soft-deleted comment leaves the thread");
            thread.Comments.Should().Contain(c => c.Id == sibling.Id, "siblings are untouched");
        }

        // Exactly ONE ReapDeletedComment sits Scheduled in durable storage for this comment.
        (await CountScheduledReapEnvelopesAsync(posted.Id)).Should().Be(1,
            "the DELETE schedules exactly one ReapDeletedComment (+30s) for this comment");
    }

    [Fact]
    public async Task Allow_idempotent_replay_of_own_deleted_comment_is_204_no_op_without_a_second_reaper()
    {
        var s = await CreateSharedScenarioAsync("del-idem");
        var posted = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Delete me twice");

        using (var first = await SendAsync(HttpMethod.Delete, CommentPath(posted.Id), TokenFor(s.Editor)))
        {
            first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var afterFirst = await LoadCommentRowAsync(posted.Id);
        var originalDeletedAt = afterFirst!.DeletedAt;
        originalDeletedAt.Should().NotBeNull();

        using (var second = await SendAsync(HttpMethod.Delete, CommentPath(posted.Id), TokenFor(s.Editor)))
        {
            second.StatusCode.Should().Be(HttpStatusCode.NoContent,
                "re-deleting the caller's OWN tombstone is the idempotent no-op 204, never 404 (R2/R5)");
        }

        var afterSecond = await LoadCommentRowAsync(posted.Id);
        afterSecond!.DeletedAt.Should().Be(originalDeletedAt, "the no-op replay does not re-stamp the tombstone");
        (await CountScheduledReapEnvelopesAsync(posted.Id)).Should().Be(1,
            "the idempotent replay does NOT schedule a second reaper");
    }

    [Fact]
    public async Task Deny_non_author_owner_is_403_and_the_comment_survives()
    {
        var s = await CreateSharedScenarioAsync("del-own");
        var posted = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Not the owner's to delete");

        using var response = await SendAsync(HttpMethod.Delete, CommentPath(posted.Id), TokenFor(s.Owner));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "even the project OWNER may not delete someone else's comment (FR-075)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
        (await LoadCommentRowAsync(posted.Id))!.DeletedAt.Should().BeNull("the denied delete changed nothing");
    }

    [Fact]
    public async Task Deny_non_author_editor_is_403()
    {
        var s = await CreateSharedScenarioAsync("del-e2");
        var other = await CreateUserAsync("g-del-e2-x", "del-e2-x@example.com", "Editor E2");
        await SeedMembershipAsync(s.Project.Id, other, MembershipRoles.Editor);
        var posted = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Mine alone");

        using var response = await SendAsync(HttpMethod.Delete, CommentPath(posted.Id), TokenFor(other));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a same-role non-author is 403 (FR-075)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
    }

    [Fact]
    public async Task Deny_demoted_author_viewer_is_403_the_role_floor_runs_first()
    {
        var s = await CreateSharedScenarioAsync("del-dem");
        var posted = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Authored as editor");
        await ChangeMembershipRoleRowAsync(s.Project.Id, s.Editor, MembershipRoles.Viewer);

        using var response = await SendAsync(HttpMethod.Delete, CommentPath(posted.Id), TokenFor(s.Editor));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "RequireRole(Editor) runs FIRST — a demoted-to-viewer author is 403 (H1/R4)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
        (await LoadCommentRowAsync(posted.Id))!.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Deny_former_member_author_is_404_membership_loss_beats_the_author_grant()
    {
        var s = await CreateSharedScenarioAsync("del-fm");
        var posted = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Posted before removal");
        await RemoveMembershipRowAsync(s.Project.Id, s.Editor);

        using var response = await SendAsync(HttpMethod.Delete, CommentPath(posted.Id), TokenFor(s.Editor));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a former member is 404'd at step 1, BEFORE the author check (FR-066 > FR-075)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
        (await LoadCommentRowAsync(posted.Id))!.DeletedAt.Should().BeNull("the denied delete changed nothing");
    }

    [Fact]
    public async Task Deny_absent_comment_is_404()
    {
        var s = await CreateSharedScenarioAsync("del-abs");

        using var response = await SendAsync(HttpMethod.Delete, CommentPath(Guid.CreateVersion7()), TokenFor(s.Editor));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }
}
