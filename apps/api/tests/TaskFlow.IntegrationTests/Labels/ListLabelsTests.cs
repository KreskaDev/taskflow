using System.Net;
using FluentAssertions;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests.Labels;

/// <summary>
/// Allow + per-user-isolation coverage for <c>GET /api/labels</c> (operationId <c>listLabels</c>, slice 006).
/// </summary>
public sealed class ListLabelsTests : SharingTestBase
{
    private static string LabelPath(Guid id) => $"/api/labels/{id}";

    [Fact]
    public async Task Allow_lists_only_the_callers_labels_ordered_by_name()
    {
        var user = await CreateUserAsync("g-ll-o", "llo@example.com", "Owner");
        var token = TokenFor(user);
        await SendAsync(HttpMethod.Put, LabelPath(Guid.CreateVersion7()), token, new { name = "Beta" });
        await SendAsync(HttpMethod.Put, LabelPath(Guid.CreateVersion7()), token, new { name = "Alpha" });

        using var response = await SendAsync(HttpMethod.Get, "/api/labels", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadLabelsAsync()).Select(l => l.Name).Should().Equal("Alpha", "Beta");
    }

    [Fact]
    public async Task Per_user_isolation_another_users_labels_are_absent()
    {
        var alice = await CreateUserAsync("g-ll-a", "lla@example.com", "Alice");
        var bob = await CreateUserAsync("g-ll-b", "llb@example.com", "Bob");
        await SendAsync(HttpMethod.Put, LabelPath(Guid.CreateVersion7()), TokenFor(alice), new { name = "AliceLabel" });

        using var response = await SendAsync(HttpMethod.Get, "/api/labels", TokenFor(bob));

        (await response.ReadLabelsAsync()).Should().BeEmpty("labels are per-user (Tier A, FR-065)");
    }

    [Fact]
    public async Task Deny_no_valid_session_is_401()
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/labels", TestJwtHelper.WrongKey("nobody"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
    }
}
