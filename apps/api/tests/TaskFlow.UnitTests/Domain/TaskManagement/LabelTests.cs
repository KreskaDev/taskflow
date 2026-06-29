using FluentAssertions;
using TaskFlow.Domain.IdentityAccess;
using Label = TaskFlow.Domain.TaskManagement.Label;
using LabelId = TaskFlow.Domain.TaskManagement.LabelId;

namespace TaskFlow.UnitTests.Domain.TaskManagement;

/// <summary>
/// Unit coverage for the <see cref="Label"/> aggregate (ENT-04, slice 006): name normalization (trim, ≤ 50,
/// case-folded uniqueness key), create/edit behavior, and the <see cref="LabelId"/> round-trip.
/// </summary>
public sealed class LabelTests
{
    private static readonly UserId Owner = UserId.New();
    private static readonly DateTime Now = new(2026, 6, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_trims_the_name_and_derives_the_normalized_form()
    {
        var label = Label.Create(LabelId.New(), Owner, "  Urgent  ", "red", Now);

        label.Name.Should().Be("Urgent");
        label.NameNormalized.Should().Be("URGENT");
        label.Color.Should().Be("red");
        label.OwnerId.Should().Be(Owner);
        label.CreatedAt.Should().Be(Now);
        label.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_allows_a_null_color()
    {
        var label = Label.Create(LabelId.New(), Owner, "Tag", color: null, Now);

        label.Color.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_name(string name)
    {
        var act = () => Label.Create(LabelId.New(), Owner, name, null, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_a_name_longer_than_50_chars()
    {
        var act = () => Label.Create(LabelId.New(), Owner, new string('x', 51), null, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Edit_renames_recolors_and_updates_the_normalized_form()
    {
        var label = Label.Create(LabelId.New(), Owner, "Old", "red", Now);
        var later = Now.AddMinutes(5);

        label.Edit("  New Name ", "blue", later);

        label.Name.Should().Be("New Name");
        label.NameNormalized.Should().Be("NEW NAME");
        label.Color.Should().Be("blue");
        label.UpdatedAt.Should().Be(later);
        label.CreatedAt.Should().Be(Now, "create time is immutable");
    }

    [Fact]
    public void NormalizeForUniqueness_is_case_insensitive_and_trimmed()
    {
        Label.NormalizeForUniqueness("  Work  ").Should().Be(Label.NormalizeForUniqueness("work"));
    }

    [Fact]
    public void LabelId_round_trips()
    {
        var value = Guid.CreateVersion7();

        LabelId.From(value).Value.Should().Be(value);
    }
}
