using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;
using CommentId = TaskFlow.Domain.TaskManagement.CommentId;

namespace TaskFlow.IntegrationTests.Comments;

/// <summary>
/// Allow + deny coverage (T024, US-14) for <c>GET /api/tasks/{taskId}/comments</c> (operationId
/// <c>listTaskComments</c>, slice 009). Readable by ANY current member (viewer+, R3): the FULL chronological
/// thread with author identity (tombstone-safe, R11/R15), typed mention tokens, and the caller-scoped
/// <c>canEdit</c>. Excludes soft-deleted rows (<c>deleted_at IS NULL</c> — R5). A non-member / former member /
/// personal task / absent task → 404, NOT an empty thread (no existence leak). NEVER echoes an email
/// (Constitution XI). RED-FIRST: fails until T025 lands.
/// </summary>
public sealed class ListTaskCommentsTests : CommentsTestBase
{
    [Fact]
    public async Task Allow_viewer_reads_the_full_thread_chronologically_with_caller_scoped_canEdit()
    {
        var s = await CreateSharedScenarioAsync("list-ok");
        var first = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "First!", s.Viewer.Value);
        var second = await PostCommentAsync(TokenFor(s.Owner), s.TaskId, "Second.");

        using var response = await SendAsync(HttpMethod.Get, CommentsPath(s.TaskId), TokenFor(s.Viewer));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "any current member (viewer+) reads the thread");
        var thread = await response.ReadCommentListAsync();
        thread.TaskId.Should().Be(s.TaskId);
        thread.Comments.Should().HaveCount(2);
        thread.Comments.Select(c => c.Id).Should().ContainInOrder(first.Id, second.Id);

        var editorComment = thread.Comments[0];
        editorComment.AuthorId.Should().Be(s.Editor.Value);
        editorComment.AuthorDisplayName.Should().Be("Editor E");
        editorComment.Mentions.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new CommentMentionBody(s.Viewer.Value, "Viewer V"));
        editorComment.CanEdit.Should().BeFalse("the VIEWER is not the author — canEdit is caller-scoped");

        thread.Comments[1].AuthorDisplayName.Should().Be("Owner O");
        thread.Comments.Should().OnlyContain(c => !c.CanEdit, "the viewer authored none of them");
    }

    [Fact]
    public async Task CanEdit_is_true_only_for_the_callers_own_comments()
    {
        var s = await CreateSharedScenarioAsync("list-ce");
        var own = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Mine");
        var foreign = await PostCommentAsync(TokenFor(s.Owner), s.TaskId, "Someone else's");

        using var response = await SendAsync(HttpMethod.Get, CommentsPath(s.TaskId), TokenFor(s.Editor));

        var thread = await response.ReadCommentListAsync();
        thread.Comments.Single(c => c.Id == own.Id).CanEdit.Should().BeTrue();
        thread.Comments.Single(c => c.Id == foreign.Id).CanEdit.Should().BeFalse();
    }

    [Fact]
    public async Task Thread_excludes_soft_deleted_comments_while_siblings_remain()
    {
        var s = await CreateSharedScenarioAsync("list-del");
        var doomed = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Doomed");
        var sibling = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Sibling");

        using (var delete = await SendAsync(HttpMethod.Delete, CommentPath(doomed.Id), TokenFor(s.Editor)))
        {
            delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var response = await SendAsync(HttpMethod.Get, CommentsPath(s.TaskId), TokenFor(s.Owner));

        var thread = await response.ReadCommentListAsync();
        thread.Comments.Should().ContainSingle("the soft-deleted comment leaves the thread (deleted_at IS NULL, R5)")
            .Which.Id.Should().Be(sibling.Id);
    }

    [Fact]
    public async Task Tombstoned_author_renders_Deleted_user_and_the_comment_survives()
    {
        var s = await CreateSharedScenarioAsync("list-tomb");
        var posted = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Anchors the thread", s.Viewer.Value);

        // Simulate the slice-015 erasure tombstone: author_id AND the mention user_id → NULL (R11).
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Comments.SingleAsync(c => c.Id == CommentId.From(posted.Id));
            db.Entry(row).Property(nameof(TaskFlow.Domain.TaskManagement.Comment.AuthorId)).CurrentValue = null;
            var mention = row.Mentions.Single();
            db.Entry(mention).Property(nameof(TaskFlow.Domain.TaskManagement.CommentMention.UserId)).CurrentValue = null;
            await db.SaveChangesAsync();
        }

        using var response = await SendAsync(HttpMethod.Get, CommentsPath(s.TaskId), TokenFor(s.Owner));

        var thread = await response.ReadCommentListAsync();
        var comment = thread.Comments.Should().ContainSingle("the comment SURVIVES author erasure (FR-085)").Subject;
        comment.AuthorId.Should().BeNull("the author reference is a tombstone");
        comment.AuthorDisplayName.Should().Be("Deleted user", "the read model renders the neutral tombstone (R11/R15)");
        comment.Body.Should().Be("Anchors the thread");
        comment.CanEdit.Should().BeFalse("a tombstoned author matches no caller");
        comment.Mentions.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new CommentMentionBody(null, "Deleted user"));
    }

    [Fact]
    public async Task Thread_never_echoes_an_email()
    {
        var s = await CreateSharedScenarioAsync("list-mail");
        await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "No emails here", s.Viewer.Value);

        using var response = await SendAsync(HttpMethod.Get, CommentsPath(s.TaskId), TokenFor(s.Owner));

        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("@example.com", "the thread carries display names only, never emails (Constitution XI)");
    }

    [Fact]
    public async Task Deny_non_member_read_is_404_not_an_empty_thread()
    {
        var s = await CreateSharedScenarioAsync("list-nm");
        await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Members only");
        var stranger = await CreateUserAsync("g-list-nm-x", "list-nm-x@example.com", "Stranger");

        using var response = await SendAsync(HttpMethod.Get, CommentsPath(s.TaskId), TokenFor(stranger));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a non-member gets 404, NOT an empty thread — existence is not disclosed (R3)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_former_member_read_is_404()
    {
        var s = await CreateSharedScenarioAsync("list-fm");
        await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Posted while a member");
        await RemoveMembershipRowAsync(s.Project.Id, s.Editor);

        using var response = await SendAsync(HttpMethod.Get, CommentsPath(s.TaskId), TokenFor(s.Editor));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "membership loss revokes the read too (FR-066)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_personal_inbox_task_read_is_404()
    {
        var owner = await CreateUserAsync("g-list-inbox", "list-inbox@example.com", "Inbox Owner");
        var inboxTask = await SeedTaskAsync(owner, "Inbox task");

        using var response = await SendAsync(HttpMethod.Get, CommentsPath(inboxTask), TokenFor(owner));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an Inbox/personal task has no comment surface — 404 even for its owner (FR-072)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_absent_task_read_is_404()
    {
        var s = await CreateSharedScenarioAsync("list-abs");

        using var response = await SendAsync(HttpMethod.Get, CommentsPath(Guid.CreateVersion7()), TokenFor(s.Editor));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }
}
