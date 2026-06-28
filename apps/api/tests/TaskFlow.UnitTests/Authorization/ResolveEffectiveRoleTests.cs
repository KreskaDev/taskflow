using FluentAssertions;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;

namespace TaskFlow.UnitTests.Authorization;

/// <summary>
/// The centre of slice 007 (Principle IX, T009): <see cref="ResourceAuthorizationPolicy.ResolveEffectiveRole"/>
/// composes the effective role from the owner anchor ∪ the membership set ∪ none, dispatched by
/// <see cref="Project.Visibility"/> — NOT a conjunction of tiers (R8). <see cref="IResourceAuthorizationPolicy.RequireRole"/>
/// enforces the deny-shape rule (R9): non-member → 404 <see cref="NotFoundException"/>, insufficient-role
/// member → 403 <see cref="ForbiddenException"/>. The last-owner guard (<see cref="LastOwnerException"/>) is
/// the pure <see cref="Project.EnsureNotLastOwner"/>, asserted alongside.
/// </summary>
public sealed class ResolveEffectiveRoleTests
{
    private static readonly DateTime Now = new(2026, 6, 20, 9, 30, 0, DateTimeKind.Utc);

    private readonly UserId _owner = UserId.New();
    private readonly UserId _editor = UserId.New();
    private readonly UserId _viewer = UserId.New();
    private readonly UserId _stranger = UserId.New();

    private Project SharedProject()
    {
        var project = Project.Create(ProjectId.From(Guid.NewGuid()), _owner, "Work", "blue", "folder", null, Now);
        project.Share(Now);
        return project;
    }

    private Project PersonalProject()
        => Project.Create(ProjectId.From(Guid.NewGuid()), _owner, "Work", "blue", "folder", null, Now);

    private ProjectMembership[] Members(ProjectId projectId) =>
    [
        ProjectMembership.Create(ProjectMembershipId.New(), projectId, _editor, MembershipRoles.Editor, Now),
        ProjectMembership.Create(ProjectMembershipId.New(), projectId, _viewer, MembershipRoles.Viewer, Now),
    ];

    private static ResourceAuthorizationPolicy PolicyFor(UserId caller)
        => new(new StubCurrentUser(caller));

    // ── ResolveEffectiveRole — shared (the membership branch) ──

    [Fact]
    public void Shared_owner_resolves_to_Owner_without_a_row()
    {
        var project = SharedProject();
        var policy = PolicyFor(_owner);

        policy.ResolveEffectiveRole(project, Members(project.Id), _owner).Should().Be(EffectiveRole.Owner);
    }

    [Fact]
    public void Shared_editor_and_viewer_resolve_from_their_rows()
    {
        var project = SharedProject();
        var policy = PolicyFor(_owner);
        var members = Members(project.Id);

        policy.ResolveEffectiveRole(project, members, _editor).Should().Be(EffectiveRole.Editor);
        policy.ResolveEffectiveRole(project, members, _viewer).Should().Be(EffectiveRole.Viewer);
    }

    [Fact]
    public void Shared_non_member_resolves_to_None()
    {
        var project = SharedProject();
        var policy = PolicyFor(_owner);

        policy.ResolveEffectiveRole(project, Members(project.Id), _stranger).Should().Be(EffectiveRole.None);
    }

    // ── ResolveEffectiveRole — personal (the ownership branch; dispatch is NOT a conjunction) ──

    [Fact]
    public void Personal_owner_resolves_to_Owner_ignoring_any_rows()
    {
        var project = PersonalProject();
        var policy = PolicyFor(_owner);

        // Even if a (stale) membership row existed, the personal branch never reads it (R8).
        policy.ResolveEffectiveRole(project, Members(project.Id), _owner).Should().Be(EffectiveRole.Owner);
    }

    [Fact]
    public void Personal_non_owner_resolves_to_None()
    {
        var project = PersonalProject();
        var policy = PolicyFor(_owner);

        policy.ResolveEffectiveRole(project, Members(project.Id), _editor).Should().Be(EffectiveRole.None);
    }

    // ── RequireRole — deny shapes (R9) ──

    [Fact]
    public void RequireRole_viewer_attempting_a_write_is_403_forbidden()
    {
        var project = SharedProject();
        var members = Members(project.Id);

        var act = () => PolicyFor(_viewer).RequireRole(project, members, EffectiveRole.Editor);

        act.Should().Throw<ForbiddenException>("a viewer is a member but lacks the editor role (policy contract; the task-write handler is slice 008)");
    }

    [Fact]
    public void RequireRole_editor_attempting_manage_is_403_forbidden()
    {
        var project = SharedProject();
        var members = Members(project.Id);

        var act = () => PolicyFor(_editor).RequireRole(project, members, EffectiveRole.Owner);

        act.Should().Throw<ForbiddenException>("manage operations require owner (R9)");
    }

    [Fact]
    public void RequireRole_non_member_is_404_not_found()
    {
        var project = SharedProject();
        var members = Members(project.Id);

        var act = () => PolicyFor(_stranger).RequireRole(project, members, EffectiveRole.Viewer);

        act.Should().Throw<NotFoundException>("existence is not disclosed across the membership boundary (R9)");
    }

    [Fact]
    public void RequireRole_allows_a_sufficient_role()
    {
        var project = SharedProject();
        var members = Members(project.Id);

        FluentActions.Invoking(() => PolicyFor(_viewer).RequireRole(project, members, EffectiveRole.Viewer)).Should().NotThrow();
        FluentActions.Invoking(() => PolicyFor(_editor).RequireRole(project, members, EffectiveRole.Editor)).Should().NotThrow();
        FluentActions.Invoking(() => PolicyFor(_owner).RequireRole(project, members, EffectiveRole.Owner)).Should().NotThrow();
    }

    [Fact]
    public void RequireRole_on_a_personal_project_denies_a_non_owner_with_404()
    {
        // The personal arm subsumes the slice-004 ownership 404: a non-owner resolves to None → not_found.
        var project = PersonalProject();

        var act = () => PolicyFor(_stranger).RequireRole(project, Array.Empty<ProjectMembership>(), EffectiveRole.Owner);

        act.Should().Throw<NotFoundException>();
    }

    // ── Last-owner guard (R7) ──

    [Fact]
    public void EnsureNotLastOwner_rejects_the_owner_as_target_and_allows_a_member()
    {
        var project = SharedProject();

        FluentActions.Invoking(() => Project.EnsureNotLastOwner(project, _owner)).Should().Throw<LastOwnerException>();
        FluentActions.Invoking(() => Project.EnsureNotLastOwner(project, _editor)).Should().NotThrow();
    }

    private sealed class StubCurrentUser(UserId id) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public UserId Id { get; } = id;
    }
}
