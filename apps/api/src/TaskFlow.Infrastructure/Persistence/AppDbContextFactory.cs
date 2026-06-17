using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can construct an <see cref="AppDbContext"/>
/// for migration scaffolding without the API host's DI container. The connection
/// string only needs to be parseable — <c>migrations add</c> does not open a connection.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=taskflow_design;Username=postgres;Password=postgres")
            .Options;

        return new AppDbContext(options);
    }
}
