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
}
