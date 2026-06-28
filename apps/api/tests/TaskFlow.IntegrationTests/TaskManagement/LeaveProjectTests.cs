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
/// Allow + deny coverage (T027, US-12) for <c>DELETE /api/projects/{id}/membership?version=</c> — a
/// non-owner member leaves (research R10). The caller deletes their OWN row, loses ALL access (next read →
/// 404), and raises <c>MembershipRevoked</c>. The OWNER cannot leave → 409 <c>last_owner</c> (before the
/// row lookup — the owner has no row). A non-member caller → 404. VERSIONED: stale → 409.
/// </summary>
public sealed class LeaveProjectTests : SharingTestBase
{
    private static string Path(Guid id, int version) =>
        $"/api/projects/{id}/membership?version={version.ToString(CultureInfo.InvariantCulture)}";

    private async Task<(ProjectBody Project, UserId Owner)> SharedAsync(string slug)
    {
        var owner = await CreateUserAsync($"g-{slug}-o", $"{slug}o@example.com", "Owner A");
        var token = TokenFor(owner);
        return (await ShareProjectAsync(token, await CreateProjectAsync(token)), owner);
    }

    [Fact]
    public async Task Allow_a_member_leaves_and_loses_all_access()
    {
        var (project, _) = await SharedAsync("lv-ok");
        var member = await CreateUserAsync("g-lv-b", "lvb@example.com", "Member B");
        await SeedMembershipAsync(project.Id, member, MembershipRoles.Editor);

        using (var response = await SendAsync(HttpMethod.Delete, Path(project.Id, project.Version), TokenFor(member)))
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await LoadMembershipsAsync(project.Id)).Should().BeEmpty("the caller's own row is deleted");
        using var afterRead = await SendAsync(HttpMethod.Get, $"/api/projects/{project.Id}/members", TokenFor(member));
        afterRead.StatusCode.Should().Be(HttpStatusCode.NotFound, "a member who left is a non-member → 404 (R10)");
    }

    [Fact]
    public async Task Allow_leave_raises_MembershipRevoked()
    {
        var (project, _) = await SharedAsync("lv-evt");
        var member = await CreateUserAsync("g-lv-evtb", "lvevtb@example.com", "Member B");
        await SeedMembershipAsync(project.Id, member, MembershipRoles.Viewer);

        var host = Services.GetRequiredService<IHost>();
        var tracked = await host.TrackActivity().Timeout(TimeSpan.FromSeconds(10)).ExecuteAndWaitAsync(
            _ => SendAsync(HttpMethod.Delete, Path(project.Id, project.Version), TokenFor(member)));

        tracked.Sent.MessagesOf<MembershipRevoked>().Should().ContainSingle().Which.UserId.Should().Be(member);
    }

    [Fact]
    public async Task Owner_cannot_leave_is_409_last_owner()
    {
        var (project, owner) = await SharedAsync("lv-owner");

        using var response = await SendAsync(HttpMethod.Delete, Path(project.Id, project.Version), TokenFor(owner));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("last_owner", "the owner cannot leave; transfer first (R7)");
    }

    [Fact]
    public async Task Deny_non_member_caller_is_404()
    {
        var (project, _) = await SharedAsync("lv-nm");
        var stranger = await CreateUserAsync("g-lv-x", "lvx@example.com", "Stranger X");

        using var response = await SendAsync(HttpMethod.Delete, Path(project.Id, project.Version), TokenFor(stranger));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Stale_version_is_409()
    {
        var (project, _) = await SharedAsync("lv-stale");
        var member = await CreateUserAsync("g-lv-sm", "lvsm@example.com", "Member B");
        await SeedMembershipAsync(project.Id, member, MembershipRoles.Editor);

        using var response = await SendAsync(HttpMethod.Delete, Path(project.Id, project.Version + 99), TokenFor(member));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("version_conflict");
        (await LoadMembershipsAsync(project.Id)).Should().ContainSingle("the rejected leave never deleted the row");
    }

    [Fact]
    public async Task Deny_no_jwt_is_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(Path(Guid.CreateVersion7(), 0), UriKind.Relative));
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
