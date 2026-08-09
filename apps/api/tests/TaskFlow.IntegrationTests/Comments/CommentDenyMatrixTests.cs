using System.Net;
using FluentAssertions;
using TaskFlow.IntegrationTests.Infrastructure;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.Comments;

/// <summary>
/// THE SC-016 role × operation deny matrix (T026, US-14) as the first-class, mechanically-verifiable
/// artifact (data-model §3), asserted through the real handlers. This is a CONSOLIDATING suite — it
/// re-asserts the deny cells already RED-covered per-handler by T018/T020/T022/T024 in one place, so an
/// authorization regression in any cell fails THIS suite by name. Deny-shape rule (R3/R4): insufficient
/// role / non-author with sufficient role → 403 <c>forbidden</c>; non-member / former member / personal
/// task / foreign id → 404 <c>not_found</c> (no existence leak); bad content / non-member mention → 422
/// <c>validation_failed</c>. NO 409 anywhere (versionless, R2).
/// </summary>
public sealed class CommentDenyMatrixTests : CommentsTestBase
{
    private static readonly Guid[] NoMentions = [];

    [Fact]
    public async Task Matrix_403_insufficient_role_and_non_author_cells()
    {
        var s = await CreateSharedScenarioAsync("mx-403");
        var editor2 = await CreateUserAsync("g-mx-403-e2", "mx-403-e2@example.com", "Editor E2");
        await SeedMembershipAsync(s.Project.Id, editor2, MembershipRoles.Editor);
        var authored = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "The contested comment");

        // viewer-post-denied (403)
        using (var r = await SendAsync(HttpMethod.Post, CommentsPath(s.TaskId), TokenFor(s.Viewer),
            new { body = "viewer post", mentionedUserIds = NoMentions }))
        {
            r.StatusCode.Should().Be(HttpStatusCode.Forbidden, "viewer-post is denied (FR-072)");
            (await r.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
        }

        // viewer-edit-denied + viewer-delete-denied (403 — the role floor, before authorship is even reached)
        using (var r = await SendAsync(HttpMethod.Patch, CommentPath(authored.Id), TokenFor(s.Viewer),
            new { body = "viewer edit", mentionedUserIds = NoMentions }))
        {
            r.StatusCode.Should().Be(HttpStatusCode.Forbidden, "viewer-edit is denied at the role floor");
            (await r.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
        }

        using (var r = await SendAsync(HttpMethod.Delete, CommentPath(authored.Id), TokenFor(s.Viewer)))
        {
            r.StatusCode.Should().Be(HttpStatusCode.Forbidden, "viewer-delete is denied at the role floor");
            (await r.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
        }

        // non-author-OWNER-edit/delete-denied (403 — role does not override authorship, FR-075)
        using (var r = await SendAsync(HttpMethod.Patch, CommentPath(authored.Id), TokenFor(s.Owner),
            new { body = "owner edit", mentionedUserIds = NoMentions }))
        {
            r.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the non-author OWNER may not edit (FR-075)");
            (await r.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
        }

        using (var r = await SendAsync(HttpMethod.Delete, CommentPath(authored.Id), TokenFor(s.Owner)))
        {
            r.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the non-author OWNER may not delete (FR-075)");
            (await r.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
        }

        // non-author-EDITOR-edit/delete-denied (403)
        using (var r = await SendAsync(HttpMethod.Patch, CommentPath(authored.Id), TokenFor(editor2),
            new { body = "peer edit", mentionedUserIds = NoMentions }))
        {
            r.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a same-role non-author may not edit");
            (await r.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
        }

        using (var r = await SendAsync(HttpMethod.Delete, CommentPath(authored.Id), TokenFor(editor2)))
        {
            r.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a same-role non-author may not delete");
            (await r.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
        }

        (await LoadCommentRowAsync(authored.Id))!.Body.Should().Be("The contested comment",
            "no denied cell mutated the row");
        (await LoadCommentRowAsync(authored.Id))!.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Matrix_404_membership_boundary_cells()
    {
        var s = await CreateSharedScenarioAsync("mx-404");
        var stranger = await CreateUserAsync("g-mx-404-x", "mx-404-x@example.com", "Stranger");
        var authored = await PostCommentAsync(TokenFor(s.Editor), s.TaskId, "Behind the boundary");

        // non-member-read-denied + non-member-post-denied (404 — no existence leak)
        using (var r = await SendAsync(HttpMethod.Get, CommentsPath(s.TaskId), TokenFor(stranger)))
        {
            r.StatusCode.Should().Be(HttpStatusCode.NotFound, "non-member read is 404, not an empty thread");
            (await r.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
        }

        using (var r = await SendAsync(HttpMethod.Post, CommentsPath(s.TaskId), TokenFor(stranger),
            new { body = "stranger post", mentionedUserIds = NoMentions }))
        {
            r.StatusCode.Should().Be(HttpStatusCode.NotFound, "non-member post is 404");
            (await r.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
        }

        // former-member-view/edit/delete-denied (404 — membership loss beats the author grant, FR-066 > FR-075)
        await RemoveMembershipRowAsync(s.Project.Id, s.Editor);

        using (var r = await SendAsync(HttpMethod.Get, CommentsPath(s.TaskId), TokenFor(s.Editor)))
        {
            r.StatusCode.Should().Be(HttpStatusCode.NotFound, "former-member view is 404");
            (await r.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
        }

        using (var r = await SendAsync(HttpMethod.Patch, CommentPath(authored.Id), TokenFor(s.Editor),
            new { body = "former member edit", mentionedUserIds = NoMentions }))
        {
            r.StatusCode.Should().Be(HttpStatusCode.NotFound, "former-member edit is 404 — before the author check");
            (await r.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
        }

        using (var r = await SendAsync(HttpMethod.Delete, CommentPath(authored.Id), TokenFor(s.Editor)))
        {
            r.StatusCode.Should().Be(HttpStatusCode.NotFound, "former-member delete is 404 — before the author check");
            (await r.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
        }

        // personal/Inbox task cells (no comment surface, FR-072)
        var inboxTask = await SeedTaskAsync(s.Owner, "Inbox task");
        using (var r = await SendAsync(HttpMethod.Get, CommentsPath(inboxTask), TokenFor(s.Owner)))
        {
            r.StatusCode.Should().Be(HttpStatusCode.NotFound, "a personal/Inbox task has no thread — 404");
            (await r.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
        }

        using (var r = await SendAsync(HttpMethod.Post, CommentsPath(inboxTask), TokenFor(s.Owner),
            new { body = "no surface", mentionedUserIds = NoMentions }))
        {
            r.StatusCode.Should().Be(HttpStatusCode.NotFound, "a personal/Inbox task takes no post — 404");
            (await r.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
        }

        (await LoadCommentRowAsync(authored.Id))!.DeletedAt.Should().BeNull("no denied cell deleted the row");
    }

    [Fact]
    public async Task Matrix_422_content_and_mention_candidacy_cells()
    {
        var s = await CreateSharedScenarioAsync("mx-422");
        var outsider = await CreateUserAsync("g-mx-422-x", "mx-422-x@example.com", "Outsider");
        var token = TokenFor(s.Editor);

        // empty/whitespace body (422)
        using (var r = await SendAsync(HttpMethod.Post, CommentsPath(s.TaskId), token,
            new { body = "   ", mentionedUserIds = NoMentions }))
        {
            r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a whitespace-only body is 422 (FR-098)");
            (await r.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
        }

        // over-length body (422)
        using (var r = await SendAsync(HttpMethod.Post, CommentsPath(s.TaskId), token,
            new { body = new string('z', 4001), mentionedUserIds = NoMentions }))
        {
            r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "an over-length body is 422 (FR-098)");
            (await r.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
        }

        // non-member mention (422 — mention candidacy is CURRENT members only, R6)
        using (var r = await SendAsync(HttpMethod.Post, CommentsPath(s.TaskId), token,
            new { body = "ping outsider", mentionedUserIds = new[] { outsider.Value } }))
        {
            r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a non-member mention is 422 (R6)");
            (await r.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
        }

        (await CountCommentRowsAsync(s.TaskId, includeDeleted: true)).Should().Be(0,
            "no 422 cell persisted a comment");
    }
}
