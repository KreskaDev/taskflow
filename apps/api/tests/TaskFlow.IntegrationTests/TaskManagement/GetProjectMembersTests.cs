using System.Net;
using FluentAssertions;
using TaskFlow.IntegrationTests.Infrastructure;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T031, US-12) for <c>GET /api/projects/{id}/members</c> — the composed roster
/// (research R17): the owner (from <c>ownerId</c>, <c>isOwner=true</c>) ∪ the editor/viewer rows. Surfaces
/// the project <c>version</c> (the token for membership mutations, R11). Never echoes emails (Constitution
/// XI — structurally enforced by <c>MemberBody</c>). Member-only: any current member (viewer+) may read; a
/// non-member → 404.
/// </summary>
public sealed class GetProjectMembersTests : SharingTestBase
{
    private static string Path(Guid id) => $"/api/projects/{id}/members";

    [Fact]
    public async Task Allow_owner_reads_the_composed_roster()
    {
        var owner = await CreateUserAsync("g-gm-o", "gmo@example.com", "Owner A");
        var editor = await CreateUserAsync("g-gm-ed", "gmed@example.com", "Editor B");
        var viewer = await CreateUserAsync("g-gm-vw", "gmvw@example.com", "Viewer C");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);

        using var response = await SendAsync(HttpMethod.Get, Path(project.Id), token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var roster = await response.ReadMembersAsync();
        roster.ProjectId.Should().Be(project.Id);
        roster.Version.Should().Be(project.Version, "the roster surfaces the project version (R11)");
        roster.Members.Should().HaveCount(3);
        roster.Members.Should().ContainSingle(m => m.IsOwner && m.UserId == owner.Value && m.Role == "owner");
        roster.Members.Should().ContainSingle(m => m.UserId == editor.Value && m.Role == "editor" && !m.IsOwner);
        roster.Members.Should().ContainSingle(m => m.UserId == viewer.Value && m.Role == "viewer" && !m.IsOwner);
        roster.Members.Should().AllSatisfy(m => m.DisplayName.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public async Task Allow_a_viewer_member_reads_the_roster()
    {
        var owner = await CreateUserAsync("g-gm-vo", "gmvo@example.com", "Owner A");
        var viewer = await CreateUserAsync("g-gm-vmem", "gmvmem@example.com", "Viewer C");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);

        using var response = await SendAsync(HttpMethod.Get, Path(project.Id), TokenFor(viewer));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "any current member (viewer+) may read the roster (R9)");
        (await response.ReadMembersAsync()).Members.Should().HaveCount(2);
    }

    [Fact]
    public async Task Deny_a_non_member_is_404()
    {
        var owner = await CreateUserAsync("g-gm-nmo", "gmnmo@example.com", "Owner A");
        var stranger = await CreateUserAsync("g-gm-nmx", "gmnmx@example.com", "Stranger X");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));

        using var response = await SendAsync(HttpMethod.Get, Path(project.Id), TokenFor(stranger));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "existence is not disclosed across the membership boundary (R9)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_a_personal_project_has_no_members_resource_404()
    {
        var owner = await CreateUserAsync("g-gm-per", "gmper@example.com", "Owner A");
        var token = TokenFor(owner);
        var project = await CreateProjectAsync(token);

        using var response = await SendAsync(HttpMethod.Get, Path(project.Id), token);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a personal project has no /members resource");
    }

    [Fact]
    public async Task Deny_no_jwt_is_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Path(Guid.CreateVersion7()), UriKind.Relative));
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
