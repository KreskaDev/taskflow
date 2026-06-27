using FluentAssertions;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;

namespace TaskFlow.UnitTests.Domain.TaskManagement;

/// <summary>
/// Aggregate invariants for <see cref="Project"/> (ENT-02, T005). Mirrors the <c>Task</c> shape:
/// <c>Create</c> populates fields, defaults <c>Visibility=personal</c>, and starts <c>Version=0</c>;
/// every mutator stamps <c>UpdatedAt</c> and bumps <c>Version</c>; <c>OwnerId</c> is immutable;
/// <c>SoftDelete</c> is idempotent. The one-level-nesting rule (FR-012, R3) is expressed as a PURE
/// guard over facts the application layer supplies (the cross-row repository lookups live in the
/// handler, out of this layer); both failure shapes — parent-is-already-a-child and
/// project-has-children — are asserted here with zero repository. <c>Unarchive</c> nulls
/// <c>ParentId</c> when the parent is still hidden (R9).
/// </summary>
public sealed class ProjectTests
{
    private static readonly DateTime CreatedInstant = new(2026, 1, 2, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime MutateInstant = new(2026, 6, 20, 9, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime LaterInstant = new(2026, 6, 21, 10, 0, 0, DateTimeKind.Utc);

    private static Project NewProject(string name = "Work", ProjectId? parentId = null)
        => Project.Create(ProjectId.From(Guid.NewGuid()), UserId.New(), name, "blue", "folder", parentId, CreatedInstant);

    [Fact]
    public void Create_populates_fields_defaults_to_personal_and_stamps_both_timestamps()
    {
        var id = ProjectId.From(Guid.NewGuid());
        var owner = UserId.New();
        var parentId = ProjectId.From(Guid.NewGuid());

        var project = Project.Create(id, owner, "Work", "blue", "folder", parentId, CreatedInstant);

        project.Id.Should().Be(id);
        project.OwnerId.Should().Be(owner);
        project.Name.Should().Be("Work");
        project.Color.Should().Be("blue");
        project.Icon.Should().Be("folder");
        project.ParentId.Should().Be(parentId);
        project.Visibility.Should().Be("personal");
        project.Version.Should().Be(0);
        project.CreatedAt.Should().Be(CreatedInstant);
        project.UpdatedAt.Should().Be(CreatedInstant);
        project.ArchivedAt.Should().BeNull();
        project.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Create_top_level_leaves_parent_id_null()
    {
        var project = Project.Create(ProjectId.From(Guid.NewGuid()), UserId.New(), "Work", "blue", "folder", parentId: null, CreatedInstant);

        project.ParentId.Should().BeNull();
    }

    [Fact]
    public void Create_trims_the_name()
    {
        var project = Project.Create(ProjectId.From(Guid.NewGuid()), UserId.New(), "  Work  ", "blue", "folder", parentId: null, CreatedInstant);

        project.Name.Should().Be("Work");
    }

    [Fact]
    public void Create_accepts_a_name_of_exactly_200_characters()
    {
        var name = new string('x', 200);

        var project = Project.Create(ProjectId.From(Guid.NewGuid()), UserId.New(), name, "blue", "folder", parentId: null, CreatedInstant);

        project.Name.Should().Be(name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_name(string blank)
    {
        var act = () => Project.Create(ProjectId.From(Guid.NewGuid()), UserId.New(), blank, "blue", "folder", parentId: null, CreatedInstant);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_a_name_longer_than_200_characters_after_trim()
    {
        var tooLong = new string('x', 201);

        var act = () => Project.Create(ProjectId.From(Guid.NewGuid()), UserId.New(), tooLong, "blue", "folder", parentId: null, CreatedInstant);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Edit_updates_the_mutable_fields_together_and_bumps_version_and_updated_at()
    {
        var project = NewProject();
        var newParent = ProjectId.From(Guid.NewGuid());

        project.Edit("Home", "green", "house", newParent, MutateInstant);

        project.Name.Should().Be("Home");
        project.Color.Should().Be("green");
        project.Icon.Should().Be("house");
        project.ParentId.Should().Be(newParent);
        project.Version.Should().Be(1);
        project.UpdatedAt.Should().Be(MutateInstant);
    }

    [Fact]
    public void Edit_trims_the_name()
    {
        var project = NewProject();

        project.Edit("  Home  ", "green", "house", parentId: null, MutateInstant);

        project.Name.Should().Be("Home");
    }

    [Fact]
    public void Edit_to_top_level_nulls_the_parent_id()
    {
        var project = NewProject(parentId: ProjectId.From(Guid.NewGuid()));

        project.Edit("Work", "blue", "folder", parentId: null, MutateInstant);

        project.ParentId.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Edit_rejects_a_blank_name(string blank)
    {
        var project = NewProject();

        var act = () => project.Edit(blank, "blue", "folder", parentId: null, MutateInstant);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EnsureNestingAllowed_passes_for_a_top_level_project_with_no_parent()
    {
        var act = () => Project.EnsureNestingAllowed(parentId: null, parentIsTopLevel: true, projectHasChildren: false);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureNestingAllowed_passes_for_a_child_of_a_top_level_parent_when_it_has_no_children()
    {
        var parentId = ProjectId.From(Guid.NewGuid());

        var act = () => Project.EnsureNestingAllowed(parentId, parentIsTopLevel: true, projectHasChildren: false);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureNestingAllowed_rejects_when_the_chosen_parent_is_itself_a_child()
    {
        var parentId = ProjectId.From(Guid.NewGuid());

        // Failure shape 1: parent-is-already-a-child — setting it as parent would create a grandchild.
        var act = () => Project.EnsureNestingAllowed(parentId, parentIsTopLevel: false, projectHasChildren: false);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureNestingAllowed_rejects_when_the_project_being_parented_has_children()
    {
        var parentId = ProjectId.From(Guid.NewGuid());

        // Failure shape 2: project-has-children — giving it a parent would push its children to depth 2.
        var act = () => Project.EnsureNestingAllowed(parentId, parentIsTopLevel: true, projectHasChildren: true);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Archive_stamps_archived_at_and_bumps_version_and_updated_at()
    {
        var project = NewProject();

        project.Archive(MutateInstant);

        project.ArchivedAt.Should().Be(MutateInstant);
        project.Version.Should().Be(1);
        project.UpdatedAt.Should().Be(MutateInstant);
    }

    [Fact]
    public void Unarchive_clears_archived_at_and_keeps_parent_when_parent_is_visible()
    {
        var parentId = ProjectId.From(Guid.NewGuid());
        var project = NewProject(parentId: parentId);
        project.Archive(MutateInstant);

        project.Unarchive(parentStillHidden: false, LaterInstant);

        project.ArchivedAt.Should().BeNull();
        project.ParentId.Should().Be(parentId, "the parent is visible, so the child stays nested");
        project.Version.Should().Be(2);
        project.UpdatedAt.Should().Be(LaterInstant);
    }

    [Fact]
    public void Unarchive_orphans_to_top_level_when_the_parent_is_still_hidden()
    {
        var parentId = ProjectId.From(Guid.NewGuid());
        var project = NewProject(parentId: parentId);
        project.Archive(MutateInstant);

        // R9: a child whose parent is still archived/deleted is restored TOP-LEVEL.
        project.Unarchive(parentStillHidden: true, LaterInstant);

        project.ArchivedAt.Should().BeNull();
        project.ParentId.Should().BeNull("the parent is still hidden, so the child is promoted to top-level");
    }

    [Fact]
    public void Unarchive_of_a_top_level_project_leaves_parent_null_regardless_of_the_flag()
    {
        var project = NewProject(parentId: null);
        project.Archive(MutateInstant);

        project.Unarchive(parentStillHidden: true, LaterInstant);

        project.ParentId.Should().BeNull();
    }

    [Fact]
    public void SoftDelete_stamps_deleted_at_and_bumps_version_and_updated_at()
    {
        var project = NewProject();

        project.SoftDelete(MutateInstant);

        project.DeletedAt.Should().Be(MutateInstant);
        project.Version.Should().Be(1);
        project.UpdatedAt.Should().Be(MutateInstant);
    }

    [Fact]
    public void SoftDelete_is_idempotent_a_second_call_changes_nothing()
    {
        var project = NewProject();
        project.SoftDelete(MutateInstant);

        project.SoftDelete(LaterInstant);

        project.DeletedAt.Should().Be(MutateInstant, "deleted_at is set once and never re-stamped");
        project.Version.Should().Be(1, "the second soft-delete is a guarded no-op");
        project.UpdatedAt.Should().Be(MutateInstant, "the no-op does not re-stamp updated_at");
    }

    [Fact]
    public void OwnerId_is_immutable_across_every_mutator()
    {
        var owner = UserId.New();
        var project = Project.Create(ProjectId.From(Guid.NewGuid()), owner, "Work", "blue", "folder", parentId: null, CreatedInstant);

        project.Edit("Home", "green", "house", parentId: null, MutateInstant);
        project.Archive(MutateInstant);
        project.Unarchive(parentStillHidden: false, MutateInstant);
        project.SoftDelete(MutateInstant);

        project.OwnerId.Should().Be(owner);
    }
}
