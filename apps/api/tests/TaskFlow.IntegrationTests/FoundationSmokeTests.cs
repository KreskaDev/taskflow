using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests;

/// <summary>
/// Phase 2 foundation verification: proves the full API host actually boots
/// (Wolverine durable messaging + EF Core/Npgsql + JWT bearer all wire up), that
/// the OpenAPI document is served, and that the initial migration applied and
/// seeded the tombstone identity (T016).
/// </summary>
public sealed class FoundationSmokeTests : IntegrationTestBase
{
    [Fact]
    public async Task Host_boots_and_serves_the_openapi_document()
    {
        var response = await Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Initial_migration_seeded_the_tombstone_identity()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tombstone = await db.Users.SingleOrDefaultAsync(u => u.Id == UserId.Tombstone);

        tombstone.Should().NotBeNull();
        tombstone!.DisplayName.Should().Be("Deleted User");
    }
}
