using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskFlow.IntegrationTests.Infrastructure;
using Wolverine.Tracking;
using UserMentioned = TaskFlow.Domain.TaskManagement.Events.UserMentioned;

namespace TaskFlow.IntegrationTests.Comments;

/// <summary>
/// Allow + deny coverage (T018, US-14) for <c>POST /api/tasks/{taskId}/comments</c> (operationId
/// <c>postTaskComment</c>, slice 009). Editor/owner-only on the parent SHARED project (viewer → 403,
/// non-member / personal task / foreign → 404 — the reused slice-007 dispatch, R3); the server MINTS the
/// <c>CommentId</c>; `body` is required non-whitespace ≤ 4000 (422, no thread entry); every mention id must
/// be a CURRENT member (422, no comment created); a genuine mention raises ONE <c>UserMentioned</c> (added
/// set minus self — R6/R7). RED-FIRST: fails until T019 (PostComment vertical + CommentEndpoints) lands.
/// </summary>
public sealed class PostCommentTests : CommentsTestBase
{
    [Fact]
    public async Task Allow_editor_posts_200_with_server_minted_id_and_stamped_created_at()
    {
        var s = await CreateSharedScenarioAsync("post-ed");

        var posted = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Looks **good** to me");

        posted.Id.Should().NotBeEmpty("the server mints the CommentId (UUIDv7, R1)");
        posted.TaskId.Should().Be(s.TaskId);
        posted.AuthorId.Should().Be(s.Editor.Value);
        posted.AuthorDisplayName.Should().Be("Editor E");
        posted.Body.Should().Be("Looks **good** to me");
        posted.Mentions.Should().BeEmpty();
        posted.EditedAt.Should().BeNull("a fresh post has never been edited");
        posted.CanEdit.Should().BeTrue("the caller is the author");

        var stored = await LoadCommentRowAsync(posted.Id);
        stored.Should().NotBeNull("the post persisted a comment row");
        stored!.CreatedAt.Should().NotBe(default, "created_at is stamped server-side");
        stored.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Allow_owner_posts_200()
    {
        var s = await CreateSharedScenarioAsync("post-ow");

        var posted = await PostCommentAsync(TokenFor(s.Owner), s.TaskId, "Owner weighs in");

        posted.AuthorId.Should().Be(s.Owner.Value);
        posted.CanEdit.Should().BeTrue();
    }

    [Fact]
    public async Task Deny_viewer_post_is_403_forbidden()
    {
        var s = await CreateSharedScenarioAsync("post-vw");

        using var response = await SendAsync(
            HttpMethod.Post, CommentsPath(s.TaskId), TokenFor(s.Viewer),
            new { body = "A viewer must not post", mentionedUserIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "post = editor+ (FR-072); a viewer member is 403");
        response.MediaType().Should().Be("application/problem+json");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
        (await CountCommentRowsAsync(s.TaskId, includeDeleted: true)).Should().Be(0, "the denied post created nothing");
    }

    [Fact]
    public async Task Deny_non_member_post_is_404_not_found()
    {
        var s = await CreateSharedScenarioAsync("post-nm");
        var stranger = await CreateUserAsync("g-post-nm-x", "post-nm-x@example.com", "Stranger X");

        using var response = await SendAsync(
            HttpMethod.Post, CommentsPath(s.TaskId), TokenFor(stranger),
            new { body = "Not my project", mentionedUserIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a non-member is 404 — existence not disclosed (R3)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_personal_inbox_task_is_404_no_comment_surface()
    {
        var owner = await CreateUserAsync("g-post-inbox", "post-inbox@example.com", "Inbox Owner");
        var inboxTask = await SeedTaskAsync(owner, "My inbox task");

        using var response = await SendAsync(
            HttpMethod.Post, CommentsPath(inboxTask), TokenFor(owner),
            new { body = "No thread here", mentionedUserIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an Inbox/unprojected task has NO comment surface, even for its owner (FR-072)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_personal_project_task_is_404_no_comment_surface()
    {
        var owner = await CreateUserAsync("g-post-pp", "post-pp@example.com", "Personal Owner");
        var token = TokenFor(owner);
        var project = await CreateProjectAsync(token); // NOT shared — personal visibility.
        var taskId = await SeedTaskUnderProjectAsync(owner, project.Id, "Personal-project task", "a0");

        using var response = await SendAsync(
            HttpMethod.Post, CommentsPath(taskId), token,
            new { body = "Still no thread", mentionedUserIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a personal-visibility project has no comment surface even for its owner (the CommentAccessGuards departure)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_absent_task_is_404()
    {
        var s = await CreateSharedScenarioAsync("post-abs");

        using var response = await SendAsync(
            HttpMethod.Post, CommentsPath(Guid.CreateVersion7()), TokenFor(s.Editor),
            new { body = "Ghost task", mentionedUserIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Validation_empty_and_whitespace_only_body_is_422_and_creates_no_thread_entry()
    {
        var s = await CreateSharedScenarioAsync("post-empty");
        var token = TokenFor(s.Editor);

        foreach (var body in new[] { "", "   ", "\n\t " })
        {
            using var response = await SendAsync(
                HttpMethod.Post, CommentsPath(s.TaskId), token,
                new { body, mentionedUserIds = Array.Empty<Guid>() });

            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
                $"an empty/whitespace-only body (variant length {body.Length}) is rejected (FR-098)");
            (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
        }

        (await CountCommentRowsAsync(s.TaskId, includeDeleted: true)).Should().Be(0, "an empty body creates NO thread entry");
    }

    [Fact]
    public async Task Validation_over_length_body_is_422()
    {
        var s = await CreateSharedScenarioAsync("post-long");

        using var response = await SendAsync(
            HttpMethod.Post, CommentsPath(s.TaskId), TokenFor(s.Editor),
            new { body = new string('x', 4001), mentionedUserIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "body > 4000 chars is rejected (FR-098)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
        (await CountCommentRowsAsync(s.TaskId, includeDeleted: true)).Should().Be(0);
    }

    [Fact]
    public async Task Validation_non_member_mention_is_422_and_no_comment_is_created()
    {
        var s = await CreateSharedScenarioAsync("post-nmm");
        var outsider = await CreateUserAsync("g-post-nmm-x", "post-nmm-x@example.com", "Outsider");

        using var response = await SendAsync(
            HttpMethod.Post, CommentsPath(s.TaskId), TokenFor(s.Editor),
            new { body = "Pinging someone outside", mentionedUserIds = new[] { outsider.Value } });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "a mention id that is not a CURRENT member is 422 (R6)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
        (await CountCommentRowsAsync(s.TaskId, includeDeleted: true)).Should().Be(0, "no comment is created on a mention failure");
    }

    [Fact]
    public async Task Self_mention_is_allowed_and_persisted_but_raises_no_event()
    {
        var s = await CreateSharedScenarioAsync("post-self");
        var host = Services.GetRequiredService<IHost>();

        var tracked = await host.TrackActivity().Timeout(TimeSpan.FromSeconds(10)).ExecuteAndWaitAsync(
            _ => SendAsync(HttpMethod.Post, CommentsPath(s.TaskId), TokenFor(s.Editor),
                new { body = "Note to self", mentionedUserIds = new[] { s.Editor.Value } }));

        tracked.Sent.MessagesOf<UserMentioned>().Should().BeEmpty("a self-mention notifies no one (R6/R7)");

        var live = await CountCommentRowsAsync(s.TaskId);
        live.Should().Be(1, "the self-mention post itself succeeds");
    }

    [Fact]
    public async Task Valid_mention_raises_UserMentioned_with_the_added_set_and_actor()
    {
        var s = await CreateSharedScenarioAsync("post-ev");
        var host = Services.GetRequiredService<IHost>();

        var tracked = await host.TrackActivity().Timeout(TimeSpan.FromSeconds(10)).ExecuteAndWaitAsync(
            _ => SendAsync(HttpMethod.Post, CommentsPath(s.TaskId), TokenFor(s.Editor),
                new { body = "Ping @viewer", mentionedUserIds = new[] { s.Viewer.Value } }));

        var ev = tracked.Sent.MessagesOf<UserMentioned>().Should().ContainSingle().Subject;
        ev.MentionedUserIds.Select(u => u.Value).Should().BeEquivalentTo([s.Viewer.Value]);
        ev.ActorUserId.Value.Should().Be(s.Editor.Value);
        ev.TaskId.Value.Should().Be(s.TaskId);
        ev.ProjectId.Value.Should().Be(s.Project.Id);
    }

    [Fact]
    public async Task Mentions_are_deduplicated_before_persisting()
    {
        var s = await CreateSharedScenarioAsync("post-dup");

        var posted = await PostCommentAsync(
            TokenFor(s.Editor), s.TaskId, "Double ping", s.Viewer.Value, s.Viewer.Value);

        posted.Mentions.Should().ContainSingle("the mention set is de-duplicated (R6)")
            .Which.UserId.Should().Be(s.Viewer.Value);
    }

    [Fact]
    public async Task Deny_no_jwt_is_401_with_our_envelope()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(CommentsPath(Guid.CreateVersion7()), UriKind.Relative));
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "postTaskComment is deny-by-default (FR-068)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
    }
}
