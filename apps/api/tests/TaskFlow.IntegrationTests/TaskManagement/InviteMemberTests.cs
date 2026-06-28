using System.Net;
using FluentAssertions;
using TaskFlow.IntegrationTests.Infrastructure;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T021, US-12) for <c>POST /api/projects/{id}/members</c> — invite-by-email
/// (research R4, OOS-18). The email is resolved server-side against admitted Users: a hit (not yet a
/// member) creates a row at the assignable role; an unknown email, the owner's own email, or an existing
/// member each → 422 on <c>email</c> (one shape, no enumeration oracle). Owner-only: a member caller → 403,
/// a non-member → 404. <c>owner</c> is not a representable role. VERSIONED: stale → 409.
/// </summary>
public sealed class InviteMemberTests : SharingTestBase
{
    private async Task<ProjectBody> SharedProjectAsync(string ownerToken) =>
        await ShareProjectAsync(ownerToken, await CreateProjectAsync(ownerToken));

    private static object InviteBody(string email, string role, int version) => new { email, role, version };

    private static string MembersPath(Guid id) => $"/api/projects/{id}/members";

    [Fact]
    public async Task Allow_invite_an_admitted_user_creates_a_member_row()
    {
        var owner = await CreateUserAsync("g-inv-a", "inva@example.com", "Owner A");
        var invitee = await CreateUserAsync("g-inv-b", "invb@example.com", "Member B");
        var token = TokenFor(owner);
        var project = await SharedProjectAsync(token);

        using var response = await SendAsync(HttpMethod.Post, MembersPath(project.Id), token, InviteBody("invb@example.com", MembershipRoles.Editor, project.Version));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var member = await response.ReadMemberAsync();
        member.UserId.Should().Be(invitee.Value);
        member.DisplayName.Should().Be("Member B");
        member.Role.Should().Be("editor");
        member.IsOwner.Should().BeFalse();
        (await LoadMembershipsAsync(project.Id)).Should().ContainSingle(m => m.UserId == invitee && m.Role == "editor");
    }

    [Fact]
    public async Task Allow_invite_at_viewer_role()
    {
        var owner = await CreateUserAsync("g-inv-vo", "invvo@example.com", "Owner A");
        _ = await CreateUserAsync("g-inv-vc", "invvc@example.com", "Viewer C");
        var token = TokenFor(owner);
        var project = await SharedProjectAsync(token);

        using var response = await SendAsync(HttpMethod.Post, MembersPath(project.Id), token, InviteBody("invvc@example.com", MembershipRoles.Viewer, project.Version));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadMemberAsync()).Role.Should().Be("viewer");
    }

    [Fact]
    public async Task Validation_unknown_email_is_422_with_no_row_created()
    {
        var owner = await CreateUserAsync("g-inv-uk", "invuk@example.com", "Owner A");
        var token = TokenFor(owner);
        var project = await SharedProjectAsync(token);

        using var response = await SendAsync(HttpMethod.Post, MembersPath(project.Id), token, InviteBody("nobody@example.com", MembershipRoles.Editor, project.Version));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("validation_failed");
        problem.Errors.Should().ContainKey("email");
        (await LoadMembershipsAsync(project.Id)).Should().BeEmpty("no pending record is created for an unknown email (OOS-18)");
    }

    [Fact]
    public async Task Validation_self_invite_owner_is_422()
    {
        var owner = await CreateUserAsync("g-inv-self", "invself@example.com", "Owner A");
        var token = TokenFor(owner);
        var project = await SharedProjectAsync(token);

        using var response = await SendAsync(HttpMethod.Post, MembersPath(project.Id), token, InviteBody("invself@example.com", MembershipRoles.Editor, project.Version));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Errors.Should().ContainKey("email");
    }

    [Fact]
    public async Task Validation_duplicate_member_is_422()
    {
        var owner = await CreateUserAsync("g-inv-dupo", "invdupo@example.com", "Owner A");
        var member = await CreateUserAsync("g-inv-dupb", "invdupb@example.com", "Member B");
        var token = TokenFor(owner);
        var project = await SharedProjectAsync(token);
        await SeedMembershipAsync(project.Id, member, MembershipRoles.Editor);

        using var response = await SendAsync(HttpMethod.Post, MembersPath(project.Id), token, InviteBody("invdupb@example.com", MembershipRoles.Viewer, project.Version));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Errors.Should().ContainKey("email");
        (await LoadMembershipsAsync(project.Id)).Should().ContainSingle(m => m.UserId == member && m.Role == "editor", "the rejected invite never changed the existing row");
    }

    [Fact]
    public async Task Validation_role_outside_editor_viewer_is_422()
    {
        var owner = await CreateUserAsync("g-inv-role", "invrole@example.com", "Owner A");
        _ = await CreateUserAsync("g-inv-roleb", "invroleb@example.com", "Member B");
        var token = TokenFor(owner);
        var project = await SharedProjectAsync(token);

        using var response = await SendAsync(HttpMethod.Post, MembersPath(project.Id), token, InviteBody("invroleb@example.com", "owner", project.Version));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "owner is not a representable MembershipRole (R2)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task Deny_an_editor_member_cannot_invite_403()
    {
        var owner = await CreateUserAsync("g-inv-edo", "invedo@example.com", "Owner A");
        var editor = await CreateUserAsync("g-inv-eded", "inveded@example.com", "Editor B");
        var newcomer = await CreateUserAsync("g-inv-ednew", "invednew@example.com", "Newcomer");
        var project = await SharedProjectAsync(TokenFor(owner));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        _ = newcomer;

        using var response = await SendAsync(HttpMethod.Post, MembersPath(project.Id), TokenFor(editor), InviteBody("invednew@example.com", MembershipRoles.Viewer, project.Version));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "invite is a manage op — an editor lacks the role (R9)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
    }

    [Fact]
    public async Task Deny_a_non_member_invite_is_404()
    {
        var owner = await CreateUserAsync("g-inv-nmo", "invnmo@example.com", "Owner A");
        var stranger = await CreateUserAsync("g-inv-nmx", "invnmx@example.com", "Stranger X");
        _ = await CreateUserAsync("g-inv-nmt", "invnmt@example.com", "Target");
        var project = await SharedProjectAsync(TokenFor(owner));

        using var response = await SendAsync(HttpMethod.Post, MembersPath(project.Id), TokenFor(stranger), InviteBody("invnmt@example.com", MembershipRoles.Viewer, project.Version));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a non-member is not told the shared project exists (R9)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Stale_version_is_409()
    {
        var owner = await CreateUserAsync("g-inv-stale", "invstale@example.com", "Owner A");
        _ = await CreateUserAsync("g-inv-staleb", "invstaleb@example.com", "Member B");
        var token = TokenFor(owner);
        var project = await SharedProjectAsync(token);

        using var response = await SendAsync(HttpMethod.Post, MembersPath(project.Id), token, InviteBody("invstaleb@example.com", MembershipRoles.Editor, project.Version + 99));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("version_conflict");
    }

    [Fact]
    public async Task Deny_no_jwt_is_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(MembersPath(Guid.CreateVersion7()), UriKind.Relative));
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
    }
}
