using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement.Events;
using TaskFlow.IntegrationTests.Infrastructure;
using Wolverine.Tracking;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T023, US-12) for <c>PATCH /api/projects/{id}/members/{userId}</c> — the
/// editor↔viewer role toggle (research R5/R7). Re-sending the current role is a no-op + version bump (not
/// an error). A demotion (editor→viewer) raises <c>MembershipRevoked</c>; a promotion raises none (H1).
/// The owner as target → 409 <c>last_owner</c> (before the row lookup); a target who is neither owner nor
/// member → 404. Owner-only: member caller → 403, non-member → 404. VERSIONED: stale → 409.
/// </summary>
public sealed class ChangeMemberRoleTests : SharingTestBase
{
    private static string Path(Guid id, Guid userId) => $"/api/projects/{id}/members/{userId}";

    private static object Body(string role, int version) => new { role, version };

    private async Task<(ProjectBody Project, UserId Owner)> SharedWithOwnerAsync(string slug)
    {
        var owner = await CreateUserAsync($"g-{slug}-o", $"{slug}o@example.com", "Owner A");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        return (project, owner);
    }

    [Fact]
    public async Task Allow_toggle_editor_to_viewer_and_back()
    {
        var (project, owner) = await SharedWithOwnerAsync("crt-toggle");
        var member = await CreateUserAsync("g-crt-tm", "crttm@example.com", "Member B");
        await SeedMembershipAsync(project.Id, member, MembershipRoles.Editor);
        var token = TokenFor(owner);

        using (var demote = await SendAsync(HttpMethod.Patch, Path(project.Id, member.Value), token, Body(MembershipRoles.Viewer, project.Version)))
        {
            demote.StatusCode.Should().Be(HttpStatusCode.OK);
            (await demote.ReadMemberAsync()).Role.Should().Be("viewer");
        }

        var reloaded = (await LoadProjectAsync(project.Id))!.Version;
        using var promote = await SendAsync(HttpMethod.Patch, Path(project.Id, member.Value), token, Body(MembershipRoles.Editor, reloaded));
        promote.StatusCode.Should().Be(HttpStatusCode.OK);
        (await promote.ReadMemberAsync()).Role.Should().Be("editor");
    }

    [Fact]
    public async Task Allow_resending_current_role_is_a_no_op_plus_version_bump()
    {
        var (project, owner) = await SharedWithOwnerAsync("crt-noop");
        var member = await CreateUserAsync("g-crt-nm", "crtnm@example.com", "Member B");
        await SeedMembershipAsync(project.Id, member, MembershipRoles.Editor);

        using var response = await SendAsync(HttpMethod.Patch, Path(project.Id, member.Value), TokenFor(owner), Body(MembershipRoles.Editor, project.Version));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "re-sending the current role is a no-op, not an error (R5)");
        (await response.ReadMemberAsync()).Role.Should().Be("editor");
        (await LoadProjectAsync(project.Id))!.Version.Should().Be(project.Version + 1, "the project version still bumps (R11)");
    }

    [Fact]
    public async Task Allow_demotion_raises_MembershipRevoked()
    {
        var (project, owner) = await SharedWithOwnerAsync("crt-demote");
        var member = await CreateUserAsync("g-crt-dm", "crtdm@example.com", "Member B");
        await SeedMembershipAsync(project.Id, member, MembershipRoles.Editor);

        var host = Services.GetRequiredService<IHost>();
        var tracked = await host.TrackActivity().Timeout(TimeSpan.FromSeconds(10)).ExecuteAndWaitAsync(
            _ => SendAsync(HttpMethod.Patch, Path(project.Id, member.Value), TokenFor(owner), Body(MembershipRoles.Viewer, project.Version)));

        tracked.Sent.MessagesOf<MembershipRevoked>().Should().ContainSingle()
            .Which.UserId.Should().Be(member);
    }

    [Fact]
    public async Task Allow_promotion_raises_no_event()
    {
        var (project, owner) = await SharedWithOwnerAsync("crt-promote");
        var member = await CreateUserAsync("g-crt-pm", "crtpm@example.com", "Member B");
        await SeedMembershipAsync(project.Id, member, MembershipRoles.Viewer);

        var host = Services.GetRequiredService<IHost>();
        var tracked = await host.TrackActivity().Timeout(TimeSpan.FromSeconds(10)).ExecuteAndWaitAsync(
            _ => SendAsync(HttpMethod.Patch, Path(project.Id, member.Value), TokenFor(owner), Body(MembershipRoles.Editor, project.Version)));

        tracked.Sent.MessagesOf<MembershipRevoked>().Should().BeEmpty("a promotion is access-additive (R5/H1)");
    }

    [Fact]
    public async Task Last_owner_target_is_409_last_owner()
    {
        var (project, owner) = await SharedWithOwnerAsync("crt-lo");

        using var response = await SendAsync(HttpMethod.Patch, Path(project.Id, owner.Value), TokenFor(owner), Body(MembershipRoles.Viewer, project.Version));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("last_owner", "the owner is not demotable; transfer first (R7)");
    }

    [Fact]
    public async Task Target_neither_owner_nor_member_is_404()
    {
        var (project, owner) = await SharedWithOwnerAsync("crt-ghost");
        var ghost = await CreateUserAsync("g-crt-ghost", "crtghost@example.com", "Ghost");

        using var response = await SendAsync(HttpMethod.Patch, Path(project.Id, ghost.Value), TokenFor(owner), Body(MembershipRoles.Viewer, project.Version));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_a_member_caller_is_403_and_a_non_member_is_404()
    {
        var (project, owner) = await SharedWithOwnerAsync("crt-deny");
        var editor = await CreateUserAsync("g-crt-ded", "crtded@example.com", "Editor B");
        var viewer = await CreateUserAsync("g-crt-dvw", "crtdvw@example.com", "Viewer C");
        var stranger = await CreateUserAsync("g-crt-dx", "crtdx@example.com", "Stranger X");
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);
        _ = owner;

        using (var byEditor = await SendAsync(HttpMethod.Patch, Path(project.Id, viewer.Value), TokenFor(editor), Body(MembershipRoles.Editor, project.Version)))
        {
            byEditor.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using var byStranger = await SendAsync(HttpMethod.Patch, Path(project.Id, viewer.Value), TokenFor(stranger), Body(MembershipRoles.Editor, project.Version));
        byStranger.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Stale_version_is_409()
    {
        var (project, owner) = await SharedWithOwnerAsync("crt-stale");
        var member = await CreateUserAsync("g-crt-sm", "crtsm@example.com", "Member B");
        await SeedMembershipAsync(project.Id, member, MembershipRoles.Editor);

        using var response = await SendAsync(HttpMethod.Patch, Path(project.Id, member.Value), TokenFor(owner), Body(MembershipRoles.Viewer, project.Version + 99));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("version_conflict");
    }

    [Fact]
    public async Task Deny_no_jwt_is_401()
    {
        var path = Path(Guid.CreateVersion7(), Guid.CreateVersion7());
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri(path, UriKind.Relative));
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
