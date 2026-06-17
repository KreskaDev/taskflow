using FluentAssertions;
using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.UnitTests.Domain.IdentityAccess;

/// <summary>
/// Aggregate invariants for <see cref="User"/> (ENT-06, T033): the Google subject id is the
/// immutable identity anchor, while email/display-name/avatar are refreshed from the Google
/// profile on every returning sign-in (spec Clarifications 2026-06-17).
/// </summary>
public sealed class UserTests
{
    private static readonly DateTime CreatedInstant = new(2026, 1, 2, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime RefreshInstant = new(2026, 6, 17, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_populates_profile_and_stamps_both_timestamps()
    {
        var user = User.Create("google-sub-123", "ada@example.com", "Ada Lovelace", "https://cdn/a.png", CreatedInstant);

        user.Id.Value.Should().NotBe(Guid.Empty);
        user.GoogleSubjectId.Should().Be("google-sub-123");
        user.Email.Should().Be("ada@example.com");
        user.DisplayName.Should().Be("Ada Lovelace");
        user.AvatarUrl.Should().Be("https://cdn/a.png");
        user.CreatedAt.Should().Be(CreatedInstant);
        user.UpdatedAt.Should().Be(CreatedInstant);
    }

    [Fact]
    public void Create_allows_a_null_avatar()
    {
        var user = User.Create("google-sub-123", "ada@example.com", "Ada Lovelace", avatarUrl: null, CreatedInstant);

        user.AvatarUrl.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_google_subject_id(string blank)
    {
        var act = () => User.Create(blank, "ada@example.com", "Ada Lovelace", null, CreatedInstant);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_email(string blank)
    {
        var act = () => User.Create("google-sub-123", blank, "Ada Lovelace", null, CreatedInstant);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_display_name(string blank)
    {
        var act = () => User.Create("google-sub-123", "ada@example.com", blank, null, CreatedInstant);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RefreshProfile_updates_mutable_fields_and_updated_at_but_never_the_subject_or_created_at()
    {
        var user = User.Create("google-sub-123", "ada@old.com", "Ada Old", "https://cdn/old.png", CreatedInstant);

        user.RefreshProfile("ada@new.com", "Ada New", "https://cdn/new.png", RefreshInstant);

        user.GoogleSubjectId.Should().Be("google-sub-123", "the Google subject id is the immutable identity anchor");
        user.CreatedAt.Should().Be(CreatedInstant, "creation time is stamped once and never moves");
        user.Email.Should().Be("ada@new.com");
        user.DisplayName.Should().Be("Ada New");
        user.AvatarUrl.Should().Be("https://cdn/new.png");
        user.UpdatedAt.Should().Be(RefreshInstant);
    }

    [Fact]
    public void RefreshProfile_can_clear_a_previously_set_avatar()
    {
        var user = User.Create("google-sub-123", "ada@example.com", "Ada Lovelace", "https://cdn/a.png", CreatedInstant);

        user.RefreshProfile("ada@example.com", "Ada Lovelace", avatarUrl: null, RefreshInstant);

        user.AvatarUrl.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RefreshProfile_rejects_a_blank_email(string blank)
    {
        var user = User.Create("google-sub-123", "ada@example.com", "Ada Lovelace", null, CreatedInstant);

        var act = () => user.RefreshProfile(blank, "Ada Lovelace", null, RefreshInstant);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RefreshProfile_rejects_a_blank_display_name(string blank)
    {
        var user = User.Create("google-sub-123", "ada@example.com", "Ada Lovelace", null, CreatedInstant);

        var act = () => user.RefreshProfile("ada@example.com", blank, null, RefreshInstant);

        act.Should().Throw<ArgumentException>();
    }
}
