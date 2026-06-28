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
/// Allow + deny coverage (T029, US-12) for <c>PATCH /api/projects/{id}/owner</c> — transfer ownership
/// (research R6, FR-094). The <c>ownerId</c> moves to a named current member; the new owner's row is
/// removed; the prior owner is demoted to a new <c>editor</c> row; <c>OwnerTransferred</c> is raised. The
/// target must already be a current member — a non-member or the current owner → 422. Owner-only: member
/// caller → 403, non-member → 404. VERSIONED: stale → 409.
/// </summary>
public sealed class TransferOwnershipTests : SharingTestBase
{
    private static string Path(Guid id) => $"/api/projects/{id}/owner";

    private static object Body(UserId userId, int version) => new { userId = userId.Value, version };

    private async Task<(ProjectBody Project, UserId Owner)> SharedAsync(string slug)
    {
        var owner = await CreateUserAsync($"g-{slug}-o", $"{slug}o@example.com", "Owner A");
        var token = TokenFor(owner);
        return (await ShareProjectAsync(token, await CreateProjectAsync(token)), owner);
    }

    [Fact]
    public async Task Allow_transfer_moves_owner_demotes_prior_owner_to_editor()
    {
        var (project, owner) = await SharedAsync("tr-ok");
        var target = await CreateUserAsync("g-tr-b", "trb@example.com", "Member B");
        await SeedMembershipAsync(project.Id, target, MembershipRoles.Editor);

        using var response = await SendAsync(HttpMethod.Patch, Path(project.Id), TokenFor(owner), Body(target, project.Version));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadProjectAsync()).Role.Should().Be("editor", "the prior owner (caller) is now an editor (R6)");

        (await LoadProjectAsync(project.Id))!.OwnerId.Should().Be(target, "ownerId moved to the target");
        var rows = await LoadMembershipsAsync(project.Id);
        rows.Should().ContainSingle("the new owner has no row; the prior owner gains one");
        rows.Single().UserId.Should().Be(owner);
        rows.Single().Role.Should().Be("editor", "the prior owner is demoted to editor (R6)");
    }

    [Fact]
    public async Task Allow_transfer_raises_OwnerTransferred()
    {
        var (project, owner) = await SharedAsync("tr-evt");
        var target = await CreateUserAsync("g-tr-evtb", "trevtb@example.com", "Member B");
        await SeedMembershipAsync(project.Id, target, MembershipRoles.Editor);

        var host = Services.GetRequiredService<IHost>();
        var tracked = await host.TrackActivity().Timeout(TimeSpan.FromSeconds(10)).ExecuteAndWaitAsync(
            _ => SendAsync(HttpMethod.Patch, Path(project.Id), TokenFor(owner), Body(target, project.Version)));

        var evt = tracked.Sent.MessagesOf<OwnerTransferred>().Should().ContainSingle().Subject;
        evt.PriorOwnerId.Should().Be(owner);
        evt.NewOwnerId.Should().Be(target);
    }

    [Fact]
    public async Task Validation_transfer_to_a_non_member_is_422()
    {
        var (project, owner) = await SharedAsync("tr-nm");
        var stranger = await CreateUserAsync("g-tr-x", "trx@example.com", "Stranger X");

        using var response = await SendAsync(HttpMethod.Patch, Path(project.Id), TokenFor(owner), Body(stranger, project.Version));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed", "the target must already be a current member (R6)");
    }

    [Fact]
    public async Task Validation_transfer_to_the_current_owner_is_422()
    {
        var (project, owner) = await SharedAsync("tr-self");

        using var response = await SendAsync(HttpMethod.Patch, Path(project.Id), TokenFor(owner), Body(owner, project.Version));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task Deny_member_caller_403_and_non_member_404()
    {
        var (project, owner) = await SharedAsync("tr-deny");
        var editor = await CreateUserAsync("g-tr-ded", "trded@example.com", "Editor B");
        var stranger = await CreateUserAsync("g-tr-dx", "trdx@example.com", "Stranger X");
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        _ = owner;

        using (var byEditor = await SendAsync(HttpMethod.Patch, Path(project.Id), TokenFor(editor), Body(editor, project.Version)))
        {
            byEditor.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using var byStranger = await SendAsync(HttpMethod.Patch, Path(project.Id), TokenFor(stranger), Body(editor, project.Version));
        byStranger.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Stale_version_is_409()
    {
        var (project, owner) = await SharedAsync("tr-stale");
        var target = await CreateUserAsync("g-tr-sm", "trsm@example.com", "Member B");
        await SeedMembershipAsync(project.Id, target, MembershipRoles.Editor);

        using var response = await SendAsync(HttpMethod.Patch, Path(project.Id), TokenFor(owner), Body(target, project.Version + 99));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("version_conflict");
        (await LoadProjectAsync(project.Id))!.OwnerId.Should().Be(owner, "the rejected transfer never moved ownership");
    }

    [Fact]
    public async Task Deny_no_jwt_is_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri(Path(Guid.CreateVersion7()), UriKind.Relative));
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
