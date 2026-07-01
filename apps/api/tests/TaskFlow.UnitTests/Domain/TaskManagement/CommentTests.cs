using FluentAssertions;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;
using TaskFlow.Domain.TaskManagement.Events;

namespace TaskFlow.UnitTests.Domain.TaskManagement;

/// <summary>
/// Behavior invariants for the <see cref="Comment"/> aggregate (slice 009, T007): <see cref="Comment.Post"/>
/// mints a server id, stamps <c>created_at</c>, and raises <see cref="UserMentioned"/> for the mention set
/// MINUS any self-mention; <see cref="Comment.Edit"/> whole-replaces body + mention set, stamps
/// <c>edited_at</c> on every edit, and raises <see cref="UserMentioned"/> for the ADDED-only delta (an empty
/// added-delta raises nothing); <see cref="Comment.SoftDelete"/> stamps <c>deleted_at</c> and is idempotent
/// with NO version bump (versionless — the <c>Task.SoftDelete</c> mirror minus the version, R2/R5). The
/// mention set is de-duplicated (R6). Zero repository — cross-row work lives in the handlers.
/// </summary>
public sealed class CommentTests
{
    private static readonly DateTime PostInstant = new(2026, 6, 20, 9, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime EditInstant = new(2026, 6, 21, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DeleteInstant = new(2026, 6, 22, 11, 0, 0, DateTimeKind.Utc);

    private static readonly TaskId Task = TaskId.From(Guid.NewGuid());
    private static readonly ProjectId Project = ProjectId.From(Guid.NewGuid());

    private static Comment Post(UserId author, params UserId[] mentions)
        => Comment.Post(Task, Project, author, "Looks good to me", mentions, PostInstant);

    [Fact]
    public void Post_mints_id_stamps_created_and_raises_UserMentioned_minus_self()
    {
        var author = UserId.New();
        var mentioned = UserId.New();

        var comment = Comment.Post(Task, Project, author, "Ping @m", new[] { mentioned, author }, PostInstant);

        comment.Id.Value.Should().NotBe(Guid.Empty, "the id is server-minted (UUIDv7)");
        comment.TaskId.Should().Be(Task);
        comment.AuthorId.Should().Be(author);
        comment.Body.Should().Be("Ping @m");
        comment.CreatedAt.Should().Be(PostInstant);
        comment.EditedAt.Should().BeNull("a fresh comment is not edited");
        comment.DeletedAt.Should().BeNull();
        comment.Mentions.Select(m => m.UserId).Should().BeEquivalentTo(new UserId?[] { mentioned, author },
            "the persisted mention set keeps a self-mention (R6)");

        comment.DomainEvents.OfType<UserMentioned>().Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new UserMentioned(
                comment.Id, Task, Project, new[] { mentioned }, author),
                "the event carries the added mentions MINUS the self-mention");
    }

    [Fact]
    public void Post_with_only_a_self_mention_persists_it_but_raises_no_event()
    {
        var author = UserId.New();

        var comment = Comment.Post(Task, Project, author, "note to self @me", new[] { author }, PostInstant);

        comment.Mentions.Select(m => m.UserId).Should().BeEquivalentTo(new UserId?[] { author });
        comment.DomainEvents.OfType<UserMentioned>().Should().BeEmpty("a self-mention notifies no one");
    }

    [Fact]
    public void Post_deduplicates_the_mention_set()
    {
        var author = UserId.New();
        var mentioned = UserId.New();

        var comment = Comment.Post(Task, Project, author, "hi", new[] { mentioned, mentioned }, PostInstant);

        comment.Mentions.Select(m => m.UserId).Should().ContainSingle().Which.Should().Be(mentioned);
        comment.DomainEvents.OfType<UserMentioned>().Should().ContainSingle()
            .Which.MentionedUserIds.Should().ContainSingle().Which.Should().Be(mentioned);
    }

    [Fact]
    public void Edit_replaces_body_and_mentions_stamps_edited_and_raises_added_only_delta()
    {
        var author = UserId.New();
        var first = UserId.New();
        var second = UserId.New();
        var comment = Post(author, first);
        comment.ClearDomainEvents();

        comment.Edit("Now cc @second too", new[] { first, second }, Project, EditInstant);

        comment.Body.Should().Be("Now cc @second too");
        comment.EditedAt.Should().Be(EditInstant, "edited_at is stamped on every edit");
        comment.Mentions.Select(m => m.UserId).Should().BeEquivalentTo(new UserId?[] { first, second });
        comment.DomainEvents.OfType<UserMentioned>().Should().ContainSingle()
            .Which.MentionedUserIds.Should().BeEquivalentTo(new[] { second },
                "only the newly-ADDED mention notifies; `first` was already mentioned");
    }

    [Fact]
    public void Edit_with_no_added_mentions_stamps_edited_but_raises_no_event()
    {
        var author = UserId.New();
        var first = UserId.New();
        var comment = Post(author, first);
        comment.ClearDomainEvents();

        // A pure removal (drop `first`) — no ADDED mention.
        comment.Edit("dropped the cc", Array.Empty<UserId>(), Project, EditInstant);

        comment.EditedAt.Should().Be(EditInstant);
        comment.Mentions.Should().BeEmpty();
        comment.DomainEvents.OfType<UserMentioned>().Should().BeEmpty("no one new was mentioned");
    }

    [Fact]
    public void SoftDelete_stamps_deleted_at_and_is_idempotent()
    {
        var comment = Post(UserId.New());

        comment.SoftDelete(DeleteInstant);
        comment.DeletedAt.Should().Be(DeleteInstant);

        // Idempotent: a second soft-delete does not re-stamp (the DeleteTask posture).
        comment.SoftDelete(DeleteInstant.AddMinutes(5));
        comment.DeletedAt.Should().Be(DeleteInstant, "soft-delete is a guarded no-op once tombstoned");
    }

    [Fact]
    public void Post_rejects_an_empty_or_whitespace_body()
    {
        var act = () => Comment.Post(Task, Project, UserId.New(), "   ", Array.Empty<UserId>(), PostInstant);

        act.Should().Throw<ArgumentException>("an empty comment is unrepresentable (FR-098, last-line defence)");
    }
}
