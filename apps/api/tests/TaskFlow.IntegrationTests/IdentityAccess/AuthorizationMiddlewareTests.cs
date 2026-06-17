using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.IdentityAccess;
using TaskFlow.Application.IdentityAccess.Queries;
using TaskFlow.IntegrationTests.Infrastructure;
using Wolverine;

namespace TaskFlow.IntegrationTests.IdentityAccess;

/// <summary>
/// Proves the deny-by-default Wolverine middleware (T019, FR-068) at the message-pipeline layer,
/// independent of HTTP. Invoking any handler through <see cref="IMessageBus"/> with no authenticated
/// principal must throw <see cref="UnauthenticatedException"/> before the handler body runs — this is
/// the application-layer backstop that an HTTP gate alone cannot prove.
/// </summary>
public sealed class AuthorizationMiddlewareTests : IntegrationTestBase
{
    [Fact]
    public async Task Invoking_a_handler_through_the_bus_with_no_principal_is_denied()
    {
        using var scope = Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        // No HttpContext in this scope → ICurrentUser.IsAuthenticated is false → middleware aborts.
        var act = async () => await bus.InvokeAsync<UserProfile>(new GetCurrentUser());

        await act.Should().ThrowAsync<UnauthenticatedException>();
    }

    [Fact]
    public async Task Http_backstop_denies_an_unauthenticated_call_to_a_non_delegating_endpoint()
    {
        // /api/internal/auth-check does NOT delegate to the bus, so only the HTTP-layer backstop can
        // gate it. A no-JWT call must still be rejected 401 with our envelope — proving the backstop
        // weaves (not just the message-pipeline middleware).
        using var response = await Client.GetAsync(new Uri("/api/internal/auth-check", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.MediaType().Should().Be("application/problem+json");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
    }

    [Fact]
    public async Task Http_backstop_allows_an_authenticated_call_to_a_non_delegating_endpoint()
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/internal/auth-check", TestJwtHelper.Valid(Guid.NewGuid().ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
