using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement.Events;
using TaskFlow.IntegrationTests.Infrastructure;
using Wolverine.Tracking;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (T018, US-12) for <c>PATCH /api/projects/{id}/share</c> and
/// <c>/unshare</c> — the reversible <c>personal ↔ shared</c> round-trip (FR-058/FR-059, research R3). Share
/// is the first writable <c>shared</c> value (A's effective role <c>owner</c>, zero members); unshare flips
/// back and removes ALL membership rows in the same transaction (revoke-all, R10), retaining the owner.
/// Both raise their domain event to the outbox (R13) and are VERSIONED (stale → 409). The deny shapes are
/// dispatched by visibility: share by a non-owner → 404 (still personal); unshare by an insufficient-role
/// member → 403, by a non-member → 404 (R9).
/// </summary>
public sealed class ShareUnshareProjectTests : SharingTestBase
{
    [Fact]
    public async Task Allow_share_flips_personal_to_shared_with_owner_role_and_zero_members()
    {
        var owner = await CreateUserAsync("g-share-a1", "sharea1@example.com", "Owner A");
        var token = TokenFor(owner);
        var project = await CreateProjectAsync(token);

        using var response = await SendAsync(HttpMethod.Patch, $"/api/projects/{project.Id}/share", token, new { version = project.Version });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadProjectAsync();
        body.Visibility.Should().Be("shared", "share is the first legal write of the shared value");
        body.Role.Should().Be("owner", "the caller is the owner");
        body.Version.Should().Be(project.Version + 1);
        (await LoadMembershipsAsync(project.Id)).Should().BeEmpty("a freshly shared project has zero members (R3)");
        (await LoadProjectAsync(project.Id))!.Visibility.Should().Be("shared");
    }

    [Fact]
    public async Task Allow_share_raises_ProjectShared_to_the_outbox()
    {
        var owner = await CreateUserAsync("g-share-evt", "shareevt@example.com", "Owner A");
        var token = TokenFor(owner);
        var project = await CreateProjectAsync(token);

        var host = Services.GetRequiredService<IHost>();
        var tracked = await host.TrackActivity().Timeout(TimeSpan.FromSeconds(10)).ExecuteAndWaitAsync(
            _ => SendAsync(HttpMethod.Patch, $"/api/projects/{project.Id}/share", token, new { version = project.Version }));

        tracked.Sent.MessagesOf<ProjectShared>().Should().ContainSingle()
            .Which.ProjectId.Should().Be(ProjectId.From(project.Id));
    }

    [Fact]
    public async Task Allow_unshare_flips_back_removes_all_members_and_retains_owner()
    {
        var owner = await CreateUserAsync("g-unshare-a", "unsharea@example.com", "Owner A");
        var member = await CreateUserAsync("g-unshare-b", "unshareb@example.com", "Member B");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, member, MembershipRoles.Editor);

        using var response = await SendAsync(HttpMethod.Patch, $"/api/projects/{project.Id}/unshare", token, new { version = project.Version });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadProjectAsync();
        body.Visibility.Should().Be("personal", "unshare re-personalizes the project (FR-058)");
        (await LoadMembershipsAsync(project.Id)).Should().BeEmpty("unshare removes ALL membership rows in the same transaction (R3/R10)");
        (await LoadProjectAsync(project.Id))!.OwnerId.Should().Be(owner, "the owner is retained");
    }

    [Fact]
    public async Task Allow_unshare_raises_ProjectUnshared_to_the_outbox()
    {
        var owner = await CreateUserAsync("g-unshare-evt", "unshareevt@example.com", "Owner A");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));

        var host = Services.GetRequiredService<IHost>();
        var tracked = await host.TrackActivity().Timeout(TimeSpan.FromSeconds(10)).ExecuteAndWaitAsync(
            _ => SendAsync(HttpMethod.Patch, $"/api/projects/{project.Id}/unshare", token, new { version = project.Version }));

        tracked.Sent.MessagesOf<ProjectUnshared>().Should().ContainSingle()
            .Which.ProjectId.Should().Be(ProjectId.From(project.Id));
    }

    [Fact]
    public async Task Allow_personal_shared_reversibility_round_trips_with_zero_members()
    {
        var owner = await CreateUserAsync("g-roundtrip", "roundtrip@example.com", "Owner A");
        var token = TokenFor(owner);
        var project = await CreateProjectAsync(token);

        var shared = await ShareProjectAsync(token, project);
        using (var unshare = await SendAsync(HttpMethod.Patch, $"/api/projects/{project.Id}/unshare", token, new { version = shared.Version }))
        {
            unshare.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Re-share the now-personal project: a clean round-trip with zero members (no tombstones, R3).
        var personalAgain = await LoadProjectAsync(project.Id);
        using var reshare = await SendAsync(HttpMethod.Patch, $"/api/projects/{project.Id}/share", token, new { version = personalAgain!.Version });
        reshare.StatusCode.Should().Be(HttpStatusCode.OK);
        (await reshare.ReadProjectAsync()).Visibility.Should().Be("shared");
        (await LoadMembershipsAsync(project.Id)).Should().BeEmpty("re-share starts clean (R3)");
    }

    [Fact]
    public async Task Deny_share_by_a_non_owner_is_404_not_found()
    {
        var owner = await CreateUserAsync("g-share-owner", "shareowner@example.com", "Owner A");
        var stranger = await CreateUserAsync("g-share-x", "sharex@example.com", "Stranger X");
        var project = await CreateProjectAsync(TokenFor(owner));

        using var response = await SendAsync(HttpMethod.Patch, $"/api/projects/{project.Id}/share", TokenFor(stranger), new { version = project.Version });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a personal project's existence is not disclosed to a non-owner");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
        (await LoadProjectAsync(project.Id))!.Visibility.Should().Be("personal", "the rejected share never flipped the project");
    }

    [Fact]
    public async Task Deny_unshare_by_an_insufficient_role_member_is_403_forbidden()
    {
        var owner = await CreateUserAsync("g-unshare-owner2", "unshareowner2@example.com", "Owner A");
        var editor = await CreateUserAsync("g-unshare-ed", "unshareed@example.com", "Editor B");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);

        using var response = await SendAsync(HttpMethod.Patch, $"/api/projects/{project.Id}/unshare", TokenFor(editor), new { version = project.Version });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "unshare is a manage op — an editor member lacks the role (R9)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
        (await LoadProjectAsync(project.Id))!.Visibility.Should().Be("shared", "the rejected unshare never flipped the project");
    }

    [Fact]
    public async Task Deny_unshare_by_a_non_member_is_404_not_found()
    {
        var owner = await CreateUserAsync("g-unshare-owner3", "unshareowner3@example.com", "Owner A");
        var stranger = await CreateUserAsync("g-unshare-x", "unsharex@example.com", "Stranger X");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));

        using var response = await SendAsync(HttpMethod.Patch, $"/api/projects/{project.Id}/unshare", TokenFor(stranger), new { version = project.Version });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a non-member is not told the shared project exists (R9)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Stale_version_is_rejected_409_on_share_and_unshare()
    {
        var owner = await CreateUserAsync("g-share-stale", "sharestale@example.com", "Owner A");
        var token = TokenFor(owner);
        var project = await CreateProjectAsync(token);

        using (var staleShare = await SendAsync(HttpMethod.Patch, $"/api/projects/{project.Id}/share", token, new { version = project.Version + 99 }))
        {
            staleShare.StatusCode.Should().Be(HttpStatusCode.Conflict, "share is versioned");
            (await staleShare.ReadProblemAsync()).ErrorCode.Should().Be("version_conflict");
        }

        var shared = await ShareProjectAsync(token, project);
        using var staleUnshare = await SendAsync(HttpMethod.Patch, $"/api/projects/{project.Id}/unshare", token, new { version = shared.Version + 99 });
        staleUnshare.StatusCode.Should().Be(HttpStatusCode.Conflict, "unshare is versioned");
        (await staleUnshare.ReadProblemAsync()).ErrorCode.Should().Be("version_conflict");
    }

    [Fact]
    public async Task Resharing_an_already_shared_project_is_422_not_500()
    {
        var owner = await CreateUserAsync("g-reshare", "reshare@example.com", "Owner A");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));

        using var response = await SendAsync(HttpMethod.Patch, $"/api/projects/{project.Id}/share", token, new { version = project.Version });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "re-sharing a shared project is a client state error, not a 500");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task Unsharing_a_personal_project_is_422_not_500()
    {
        var owner = await CreateUserAsync("g-unshare-personal", "unsharepersonal@example.com", "Owner A");
        var token = TokenFor(owner);
        var project = await CreateProjectAsync(token);

        using var response = await SendAsync(HttpMethod.Patch, $"/api/projects/{project.Id}/unshare", token, new { version = project.Version });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "unsharing a personal project is a client state error, not a 500");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task Deny_no_jwt_is_rejected_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri($"/api/projects/{Guid.CreateVersion7()}/share", UriKind.Relative));
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "shareProject is deny-by-default (FR-068)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
    }
}
