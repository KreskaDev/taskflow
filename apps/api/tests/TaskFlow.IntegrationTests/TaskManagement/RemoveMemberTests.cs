using System.Globalization;
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
/// Allow + deny coverage (T025, US-12) for <c>DELETE /api/projects/{id}/members/{userId}?version=</c> —
/// remove-member (research R10). The removed user loses ALL access immediately (their next read → 404) and
/// the deletion raises <c>MembershipRevoked</c>. The owner as target → 409 <c>last_owner</c> (before the row
/// lookup); a target who is neither owner nor member → 404. Owner-only: member caller → 403, non-member →
/// 404. VERSIONED: stale → 409.
/// </summary>
public sealed class RemoveMemberTests : SharingTestBase
{
    private static string Path(Guid id, Guid userId, int version) =>
        $"/api/projects/{id}/members/{userId}?version={version.ToString(CultureInfo.InvariantCulture)}";

    private async Task<(ProjectBody Project, UserId Owner)> SharedAsync(string slug)
    {
        var owner = await CreateUserAsync($"g-{slug}-o", $"{slug}o@example.com", "Owner A");
        var token = TokenFor(owner);
        return (await ShareProjectAsync(token, await CreateProjectAsync(token)), owner);
    }

    [Fact]
    public async Task Allow_remove_deletes_the_row_and_the_removed_user_loses_all_access()
    {
        var (project, owner) = await SharedAsync("rm-ok");
        var member = await CreateUserAsync("g-rm-b", "rmb@example.com", "Member B");
        await SeedMembershipAsync(project.Id, member, MembershipRoles.Editor);

        using (var response = await SendAsync(HttpMethod.Delete, Path(project.Id, member.Value, project.Version), TokenFor(owner)))
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await LoadMembershipsAsync(project.Id)).Should().BeEmpty("the row is deleted");

        // Revoke-all: the removed user's next read of the roster → 404 (R10).
        using var afterRead = await SendAsync(HttpMethod.Get, $"/api/projects/{project.Id}/members", TokenFor(member));
        afterRead.StatusCode.Should().Be(HttpStatusCode.NotFound, "a removed member is a non-member → 404 (R10)");
    }

    [Fact]
    public async Task Allow_remove_raises_MembershipRevoked()
    {
        var (project, owner) = await SharedAsync("rm-evt");
        var member = await CreateUserAsync("g-rm-evtb", "rmevtb@example.com", "Member B");
        await SeedMembershipAsync(project.Id, member, MembershipRoles.Viewer);

        var host = Services.GetRequiredService<IHost>();
        var tracked = await host.TrackActivity().Timeout(TimeSpan.FromSeconds(10)).ExecuteAndWaitAsync(
            _ => SendAsync(HttpMethod.Delete, Path(project.Id, member.Value, project.Version), TokenFor(owner)));

        tracked.Sent.MessagesOf<MembershipRevoked>().Should().ContainSingle().Which.UserId.Should().Be(member);
    }

    [Fact]
    public async Task Last_owner_target_is_409_last_owner()
    {
        var (project, owner) = await SharedAsync("rm-lo");

        using var response = await SendAsync(HttpMethod.Delete, Path(project.Id, owner.Value, project.Version), TokenFor(owner));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("last_owner");
    }

    [Fact]
    public async Task Target_neither_owner_nor_member_is_404()
    {
        var (project, owner) = await SharedAsync("rm-ghost");
        var ghost = await CreateUserAsync("g-rm-ghost", "rmghost@example.com", "Ghost");

        using var response = await SendAsync(HttpMethod.Delete, Path(project.Id, ghost.Value, project.Version), TokenFor(owner));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_member_caller_403_and_non_member_404()
    {
        var (project, owner) = await SharedAsync("rm-deny");
        var editor = await CreateUserAsync("g-rm-ded", "rmded@example.com", "Editor B");
        var viewer = await CreateUserAsync("g-rm-dvw", "rmdvw@example.com", "Viewer C");
        var stranger = await CreateUserAsync("g-rm-dx", "rmdx@example.com", "Stranger X");
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);
        _ = owner;

        using (var byEditor = await SendAsync(HttpMethod.Delete, Path(project.Id, viewer.Value, project.Version), TokenFor(editor)))
        {
            byEditor.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using var byStranger = await SendAsync(HttpMethod.Delete, Path(project.Id, viewer.Value, project.Version), TokenFor(stranger));
        byStranger.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Stale_version_is_409()
    {
        var (project, owner) = await SharedAsync("rm-stale");
        var member = await CreateUserAsync("g-rm-sm", "rmsm@example.com", "Member B");
        await SeedMembershipAsync(project.Id, member, MembershipRoles.Editor);

        using var response = await SendAsync(HttpMethod.Delete, Path(project.Id, member.Value, project.Version + 99), TokenFor(owner));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("version_conflict");
        (await LoadMembershipsAsync(project.Id)).Should().ContainSingle("the rejected remove never deleted the row");
    }

    [Fact]
    public async Task Deny_no_jwt_is_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(Path(Guid.CreateVersion7(), Guid.CreateVersion7(), 0), UriKind.Relative));
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
