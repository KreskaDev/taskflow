using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests.IdentityAccess;

/// <summary>
/// Allow + deny coverage (SC-016) for <c>POST /api/users/ensure</c> — the BFF bootstrap call made
/// during the OAuth callback. The carrier's <c>sub</c> is the Google subject id (not a TaskFlow user id),
/// so the handler reads the subject from the request body, never <c>currentUser.Id</c>.
/// </summary>
/// <remarks>
/// The deny case here is the sharpest probe of the deny-by-default guarantee (T019): because the handler
/// never dereferences <c>currentUser.Id</c>, a middleware that failed to weave would NOT surface as a 500 —
/// the handler would run to completion and create a user. So "no JWT → exactly 401, and no row written" is
/// the assertion that proves unauthenticated account creation is impossible.
/// </remarks>
public sealed class EnsureUserTests : IntegrationTestBase
{
    private const string EnsurePath = "/api/users/ensure";

    [Fact]
    public async Task Allow_first_call_creates_a_fresh_account_with_a_server_generated_id()
    {
        const string sub = "google-sub-new-001";
        var body = new { googleSubjectId = sub, email = "ada@example.com", displayName = "Ada Lovelace", avatarUrl = "https://cdn/a.png" };

        using var response = await SendAsync(HttpMethod.Post, EnsurePath, TestJwtHelper.Valid(sub), body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.ReadProfileAsync();
        profile.Id.Should().NotBe(Guid.Empty);
        profile.Id.ToString().Should().NotBe(sub, "the returned id is a server-generated TaskFlow UserId, not the Google subject id");
        profile.Email.Should().Be("ada@example.com");
        profile.DisplayName.Should().Be("Ada Lovelace");
        profile.AvatarUrl.Should().Be("https://cdn/a.png");
    }

    [Fact]
    public async Task Allow_returning_call_matches_the_same_row_and_refreshes_the_profile()
    {
        const string sub = "google-sub-returning-002";
        var first = new { googleSubjectId = sub, email = "old@example.com", displayName = "Old Name", avatarUrl = (string?)null };
        using (var firstResponse = await SendAsync(HttpMethod.Post, EnsurePath, TestJwtHelper.Valid(sub), first))
        {
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var firstProfile = await (await SendAsync(HttpMethod.Post, EnsurePath, TestJwtHelper.Valid(sub), first)).ReadProfileAsync();

        var refreshed = new { googleSubjectId = sub, email = "new@example.com", displayName = "New Name", avatarUrl = "https://cdn/new.png" };
        using var response = await SendAsync(HttpMethod.Post, EnsurePath, TestJwtHelper.Valid(sub), refreshed);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.ReadProfileAsync();
        profile.Id.Should().Be(firstProfile.Id, "a returning sign-in matches the existing row, never creating a second account");
        profile.Email.Should().Be("new@example.com");
        profile.DisplayName.Should().Be("New Name");
        profile.AvatarUrl.Should().Be("https://cdn/new.png");
    }

    [Fact]
    public async Task Allow_a_previously_unseen_subject_creates_a_distinct_account()
    {
        var a = new { googleSubjectId = "google-sub-aaa", email = "a@example.com", displayName = "User A", avatarUrl = (string?)null };
        var b = new { googleSubjectId = "google-sub-bbb", email = "b@example.com", displayName = "User B", avatarUrl = (string?)null };

        var profileA = await (await SendAsync(HttpMethod.Post, EnsurePath, TestJwtHelper.Valid("google-sub-aaa"), a)).ReadProfileAsync();
        var profileB = await (await SendAsync(HttpMethod.Post, EnsurePath, TestJwtHelper.Valid("google-sub-bbb"), b)).ReadProfileAsync();

        profileB.Id.Should().NotBe(profileA.Id);
    }

    [Fact]
    public async Task Deny_no_jwt_is_rejected_401_with_our_envelope_and_creates_no_account()
    {
        const string sub = "google-sub-unauth-666";
        var body = new { googleSubjectId = sub, email = "intruder@example.com", displayName = "Intruder", avatarUrl = (string?)null };

        using var response = await Client.PostAsJsonAsync(EnsurePath, body);

        // Leg (a)+(b): rejected, and exactly 401 — NOT 200 (a 200 here means the middleware never wove and an
        // unauthenticated caller created a user) and NOT 500 (handler threw).
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Leg (c): the body is OUR RFC 9457 envelope, not Wolverine.Http's own result/exception handling.
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("unauthenticated");
        problem.Status.Should().Be(401);

        // The catastrophic check: no row was written for the unauthenticated subject.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.AnyAsync(u => u.GoogleSubjectId == sub))
            .Should().BeFalse("an unauthenticated request must never create an account");
    }

    [Fact]
    public async Task Deny_invalid_signature_is_rejected_401()
    {
        const string sub = "google-sub-wrongkey-777";
        var body = new { googleSubjectId = sub, email = "x@example.com", displayName = "X", avatarUrl = (string?)null };

        using var response = await SendAsync(HttpMethod.Post, EnsurePath, TestJwtHelper.WrongKey(sub), body);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.AnyAsync(u => u.GoogleSubjectId == sub)).Should().BeFalse();
    }
}
