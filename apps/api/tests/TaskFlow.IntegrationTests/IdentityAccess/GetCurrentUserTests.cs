using System.Net;
using FluentAssertions;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests.IdentityAccess;

/// <summary>
/// Allow + deny coverage (SC-013, SC-016) for <c>GET /api/users/me</c>. Unlike ensure, this handler
/// authorizes on <c>currentUser.Id</c> (the carrier <c>sub</c> is a TaskFlow user id minted by the proxy),
/// and returns 401 when that id references a row that no longer exists (a hard-deleted account).
/// </summary>
public sealed class GetCurrentUserTests : IntegrationTestBase
{
    private const string MePath = "/api/users/me";
    private const string EnsurePath = "/api/users/ensure";

    [Fact]
    public async Task Allow_returns_the_callers_own_profile()
    {
        // Bootstrap a real account; its server-generated id becomes the proxy carrier's sub.
        const string sub = "google-sub-me-100";
        var created = await (await SendAsync(
            HttpMethod.Post, EnsurePath, TestJwtHelper.Valid(sub),
            new { googleSubjectId = sub, email = "me@example.com", displayName = "Me Myself", avatarUrl = "https://cdn/me.png" }))
            .ReadProfileAsync();

        using var response = await SendAsync(HttpMethod.Get, MePath, TestJwtHelper.Valid(created.Id.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.ReadProfileAsync();
        profile.Id.Should().Be(created.Id);
        profile.Email.Should().Be("me@example.com");
        profile.DisplayName.Should().Be("Me Myself");
        profile.AvatarUrl.Should().Be("https://cdn/me.png");
    }

    [Fact]
    public async Task Deny_no_jwt_is_rejected_401_with_our_envelope()
    {
        using var response = await Client.GetAsync(new Uri(MePath, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("unauthenticated");
        problem.Status.Should().Be(401);
    }

    [Fact]
    public async Task Deny_a_valid_jwt_for_a_nonexistent_user_is_rejected_401()
    {
        // A well-formed carrier whose sub is a TaskFlow user id that was never created (or was hard-deleted).
        var ghost = Guid.NewGuid().ToString();

        using var response = await SendAsync(HttpMethod.Get, MePath, TestJwtHelper.Valid(ghost));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("unauthenticated");
    }

    [Fact]
    public async Task Deny_invalid_signature_is_rejected_401()
    {
        using var response = await SendAsync(HttpMethod.Get, MePath, TestJwtHelper.WrongKey(Guid.NewGuid().ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
