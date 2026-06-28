using FluentAssertions;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;
using TaskFlow.Domain.TaskManagement.Events;

namespace TaskFlow.UnitTests.Domain.TaskManagement;

/// <summary>
/// Sharing-behavior invariants for <see cref="Project"/> (slice 007, T007): <see cref="Project.Share"/> is
/// valid only from <c>personal</c> and raises <see cref="ProjectShared"/>; <see cref="Project.Unshare"/>
/// flips back and raises <see cref="ProjectUnshared"/> (the row removal is the handler's job, R3);
/// <see cref="Project.TransferOwnerTo"/> reassigns <c>OwnerId</c> and raises <see cref="OwnerTransferred"/>
/// (R6); the pure static guard <see cref="Project.EnsureNotLastOwner"/> rejects the owner as a target with
/// <see cref="LastOwnerException"/> and is a no-op otherwise (R7). Every behavior bumps <c>Version</c> and
/// stamps <c>UpdatedAt</c> (zero repository — the cross-row work lives in the handlers).
/// </summary>
public sealed class ProjectSharingTests
{
    private static readonly DateTime CreatedInstant = new(2026, 1, 2, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ShareInstant = new(2026, 6, 20, 9, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime LaterInstant = new(2026, 6, 21, 10, 0, 0, DateTimeKind.Utc);

    private static Project NewPersonalProject(UserId owner)
        => Project.Create(ProjectId.From(Guid.NewGuid()), owner, "Work", "blue", "folder", parentId: null, CreatedInstant);

    [Fact]
    public void Share_from_personal_flips_visibility_bumps_version_and_raises_ProjectShared()
    {
        var owner = UserId.New();
        var project = NewPersonalProject(owner);

        project.Share(ShareInstant);

        project.Visibility.Should().Be("shared");
        project.Version.Should().Be(1, "Share is the project's first mutation");
        project.UpdatedAt.Should().Be(ShareInstant);
        project.DomainEvents.OfType<ProjectShared>().Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ProjectShared(project.Id, owner));
    }

    [Fact]
    public void Share_when_already_shared_throws()
    {
        var project = NewPersonalProject(UserId.New());
        project.Share(ShareInstant);

        var act = () => project.Share(LaterInstant);

        act.Should().Throw<InvalidOperationException>("the shared value is writable only from personal (R3)");
    }

    [Fact]
    public void Unshare_from_shared_flips_back_and_raises_ProjectUnshared()
    {
        var owner = UserId.New();
        var project = NewPersonalProject(owner);
        project.Share(ShareInstant);
        var versionAfterShare = project.Version;

        project.Unshare(LaterInstant);

        project.Visibility.Should().Be("personal");
        project.Version.Should().Be(versionAfterShare + 1);
        project.OwnerId.Should().Be(owner, "unshare retains the owner (R3)");
        project.DomainEvents.OfType<ProjectUnshared>().Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ProjectUnshared(project.Id, owner));
    }

    [Fact]
    public void Unshare_when_personal_throws()
    {
        var project = NewPersonalProject(UserId.New());

        var act = () => project.Unshare(LaterInstant);

        act.Should().Throw<InvalidOperationException>("only a shared project can be unshared");
    }

    [Fact]
    public void TransferOwnerTo_reassigns_owner_bumps_version_and_raises_OwnerTransferred()
    {
        var priorOwner = UserId.New();
        var newOwner = UserId.New();
        var project = NewPersonalProject(priorOwner);
        project.Share(ShareInstant);
        var versionAfterShare = project.Version;

        project.TransferOwnerTo(newOwner, LaterInstant);

        project.OwnerId.Should().Be(newOwner, "transfer is the only legal OwnerId mutation (R6)");
        project.Version.Should().Be(versionAfterShare + 1);
        project.UpdatedAt.Should().Be(LaterInstant);
        project.DomainEvents.OfType<OwnerTransferred>().Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new OwnerTransferred(project.Id, priorOwner, newOwner));
    }

    [Fact]
    public void TransferOwnerTo_on_a_personal_project_throws()
    {
        var project = NewPersonalProject(UserId.New());

        var act = () => project.TransferOwnerTo(UserId.New(), LaterInstant);

        act.Should().Throw<InvalidOperationException>("ownership only transfers on a shared project");
    }

    [Fact]
    public void TransferOwnerTo_the_current_owner_throws()
    {
        var owner = UserId.New();
        var project = NewPersonalProject(owner);
        project.Share(ShareInstant);

        var act = () => project.TransferOwnerTo(owner, LaterInstant);

        act.Should().Throw<InvalidOperationException>("the target must differ from the current owner");
    }

    [Fact]
    public void EnsureNotLastOwner_throws_when_target_is_the_owner()
    {
        var owner = UserId.New();
        var project = NewPersonalProject(owner);

        var act = () => Project.EnsureNotLastOwner(project, owner);

        act.Should().Throw<LastOwnerException>("the sole owner cannot be removed/demoted/leave; transfer first (R7)");
    }

    [Fact]
    public void EnsureNotLastOwner_is_a_no_op_for_any_other_target()
    {
        var project = NewPersonalProject(UserId.New());

        var act = () => Project.EnsureNotLastOwner(project, UserId.New());

        act.Should().NotThrow();
    }

    [Fact]
    public void RecordMembershipChange_bumps_version_without_raising_an_event()
    {
        var project = NewPersonalProject(UserId.New());
        project.Share(ShareInstant);
        var versionAfterShare = project.Version;
        var eventsBefore = project.DomainEvents.Count;

        project.RecordMembershipChange(LaterInstant);

        project.Version.Should().Be(versionAfterShare + 1, "every membership mutation advances the single sharing token (R11)");
        project.UpdatedAt.Should().Be(LaterInstant);
        project.DomainEvents.Count.Should().Be(eventsBefore, "a bare membership-token bump raises no project-state event");
    }
}
