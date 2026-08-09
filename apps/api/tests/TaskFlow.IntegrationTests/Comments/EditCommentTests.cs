using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskFlow.IntegrationTests.Infrastructure;
using Wolverine.Tracking;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;
using UserMentioned = TaskFlow.Domain.TaskManagement.Events.UserMentioned;

namespace TaskFlow.IntegrationTests.Comments;

/// <summary>
/// Allow + deny coverage (T020, US-14) for <c>PATCH /api/comments/{commentId}</c> (operationId
/// <c>editComment</c>, slice 009). The STRICT two-step gate (R4): <c>RequireRole(Editor)</c> on the parent
/// shared project FIRST (viewer → 403, former member → 404 — FR-066 beats FR-075), then author-equality
/// (a NON-AUTHOR of any role, INCLUDING the owner, → 403). WHOLE body + mention-set replace, `edited_at`
/// stamped on every edit, LAST-WRITE-WINS (no version, NO 409 — R2); `UserMentioned` carries the
/// ADDED-only delta (removals/unchanged emit nothing — R7). RED-FIRST: fails until T021 lands.
/// </summary>
public sealed class EditCommentTests : CommentsTestBase
{
    [Fact]
    public async Task Allow_author_edits_whole_body_and_mention_set_stamping_edited_at()
    {
        var s = await CreateSharedScenarioAsync("edit-ok");
        var posted = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "First draft", s.Viewer.Value);

        using var response = await SendAsync(
            HttpMethod.Patch, CommentPath(posted.Id), TokenFor(s.Editor),
            new { body = "  Second draft  ", mentionedUserIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the author may edit their own comment (FR-075)");
        var edited = await response.ReadCommentAsync();
        edited.Id.Should().Be(posted.Id);
        edited.Body.Should().Be("Second draft", "the body is whole-replaced (and trimmed)");
        edited.Mentions.Should().BeEmpty("the mention set is whole-replaced (anti-silent-null)");
        edited.EditedAt.Should().NotBeNull("edited_at is stamped on EVERY edit");
        edited.CanEdit.Should().BeTrue();

        var stored = await LoadCommentRowAsync(posted.Id);
        stored!.Body.Should().Be("Second draft");
        stored.Mentions.Should().BeEmpty();
        stored.EditedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Edit_raises_UserMentioned_for_the_added_only_delta()
    {
        var s = await CreateSharedScenarioAsync("edit-delta");
        var extra = await CreateUserAsync("g-edit-delta-e2", "edit-delta-e2@example.com", "Editor E2");
        await SeedMembershipAsync(s.Project.Id, extra, MembershipRoles.Editor);
        var posted = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Ping one", s.Viewer.Value);

        var host = Services.GetRequiredService<IHost>();
        var tracked = await host.TrackActivity().Timeout(TimeSpan.FromSeconds(10)).ExecuteAndWaitAsync(
            _ => SendAsync(HttpMethod.Patch, CommentPath(posted.Id), TokenFor(s.Editor),
                new { body = "Ping both", mentionedUserIds = new[] { s.Viewer.Value, extra.Value } }));

        var ev = tracked.Sent.MessagesOf<UserMentioned>().Should().ContainSingle().Subject;
        ev.MentionedUserIds.Select(u => u.Value).Should().BeEquivalentTo(
            [extra.Value], "only the NEWLY-ADDED mention is notified — the kept one emits nothing (R7)");
        ev.ActorUserId.Value.Should().Be(s.Editor.Value);
    }

    [Fact]
    public async Task Edit_that_only_removes_mentions_raises_no_event()
    {
        var s = await CreateSharedScenarioAsync("edit-rm");
        var posted = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Ping viewer", s.Viewer.Value);

        var host = Services.GetRequiredService<IHost>();
        var tracked = await host.TrackActivity().Timeout(TimeSpan.FromSeconds(10)).ExecuteAndWaitAsync(
            _ => SendAsync(HttpMethod.Patch, CommentPath(posted.Id), TokenFor(s.Editor),
                new { body = "No more ping", mentionedUserIds = Array.Empty<Guid>() }));

        tracked.Sent.MessagesOf<UserMentioned>().Should().BeEmpty(
            "an edit whose added-mention delta is empty (removal only) raises NO event (R7)");
        (await LoadCommentRowAsync(posted.Id))!.Mentions.Should().BeEmpty("the removal itself is applied");
    }

    [Fact]
    public async Task Deny_non_author_owner_is_403_role_does_not_override_authorship()
    {
        var s = await CreateSharedScenarioAsync("edit-own");
        var posted = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Editor's words");

        using var response = await SendAsync(
            HttpMethod.Patch, CommentPath(posted.Id), TokenFor(s.Owner),
            new { body = "Owner rewrites history", mentionedUserIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "even the project OWNER may not edit someone else's comment (FR-075)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
        (await LoadCommentRowAsync(posted.Id))!.Body.Should().Be("Editor's words", "the denied edit changed nothing");
    }

    [Fact]
    public async Task Deny_non_author_editor_is_403()
    {
        var s = await CreateSharedScenarioAsync("edit-e2");
        var other = await CreateUserAsync("g-edit-e2-x", "edit-e2-x@example.com", "Editor E2");
        await SeedMembershipAsync(s.Project.Id, other, MembershipRoles.Editor);
        var posted = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Mine");

        using var response = await SendAsync(
            HttpMethod.Patch, CommentPath(posted.Id), TokenFor(other),
            new { body = "Peer overwrite", mentionedUserIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a same-role non-author is 403 (FR-075)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
    }

    [Fact]
    public async Task Deny_demoted_author_viewer_is_403_the_role_floor_runs_first()
    {
        // The H1 resolution: FR-075's author grant does NOT waive the role floor. An author demoted to
        // viewer fails RequireRole(Editor) FIRST → 403, even though author-equality would pass.
        var s = await CreateSharedScenarioAsync("edit-dem");
        var posted = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Authored as editor");
        await ChangeMembershipRoleRowAsync(s.Project.Id, s.Editor, MembershipRoles.Viewer);

        using var response = await SendAsync(
            HttpMethod.Patch, CommentPath(posted.Id), TokenFor(s.Editor),
            new { body = "Editing as viewer", mentionedUserIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the role floor (RequireRole(Editor)) runs FIRST — a demoted-to-viewer author is 403 (H1/R4)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
    }

    [Fact]
    public async Task Deny_former_member_author_is_404_membership_loss_beats_the_author_grant()
    {
        var s = await CreateSharedScenarioAsync("edit-fm");
        var posted = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Was a member once");
        await RemoveMembershipRowAsync(s.Project.Id, s.Editor);

        using var response = await SendAsync(
            HttpMethod.Patch, CommentPath(posted.Id), TokenFor(s.Editor),
            new { body = "Still mine?", mentionedUserIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "membership resolves LIVE in step 1 — a former member is 404'd BEFORE the author check (FR-066 > FR-075)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_absent_comment_is_404()
    {
        var s = await CreateSharedScenarioAsync("edit-abs");

        using var response = await SendAsync(
            HttpMethod.Patch, CommentPath(Guid.CreateVersion7()), TokenFor(s.Editor),
            new { body = "Ghost", mentionedUserIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_comment_on_a_now_personal_task_is_404()
    {
        var s = await CreateSharedScenarioAsync("edit-pp");
        var posted = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Posted while shared");
        await MakeProjectPersonalAsync(s.Project.Id);

        using var response = await SendAsync(
            HttpMethod.Patch, CommentPath(posted.Id), TokenFor(s.Editor),
            new { body = "Project went personal", mentionedUserIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a now-personal parent project has no comment surface (R3)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Validation_empty_and_over_length_body_is_422()
    {
        var s = await CreateSharedScenarioAsync("edit-val");
        var posted = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Valid start");

        using (var response = await SendAsync(
            HttpMethod.Patch, CommentPath(posted.Id), TokenFor(s.Editor),
            new { body = "   ", mentionedUserIds = Array.Empty<Guid>() }))
        {
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a whitespace-only body is 422");
            (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
        }

        using (var response = await SendAsync(
            HttpMethod.Patch, CommentPath(posted.Id), TokenFor(s.Editor),
            new { body = new string('y', 4001), mentionedUserIds = Array.Empty<Guid>() }))
        {
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "an over-length body is 422");
            (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
        }

        (await LoadCommentRowAsync(posted.Id))!.Body.Should().Be("Valid start", "the rejected edits changed nothing");
    }

    [Fact]
    public async Task Validation_non_member_mention_is_422_and_the_comment_is_unchanged()
    {
        var s = await CreateSharedScenarioAsync("edit-nmm");
        var outsider = await CreateUserAsync("g-edit-nmm-x", "edit-nmm-x@example.com", "Outsider");
        var posted = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Before");

        using var response = await SendAsync(
            HttpMethod.Patch, CommentPath(posted.Id), TokenFor(s.Editor),
            new { body = "After", mentionedUserIds = new[] { outsider.Value } });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a non-member mention on edit is 422 (R6)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
        (await LoadCommentRowAsync(posted.Id))!.Body.Should().Be("Before");
    }
}
