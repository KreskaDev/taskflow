using Microsoft.EntityFrameworkCore;
using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the domain write-side. Code-first migrations against this
/// context are the schema source of truth (Constitution VI). Wolverine manages its
/// own durable-messaging tables separately (auto-provisioned at runtime), so they
/// are intentionally absent from these migrations.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<TaskFlow.Domain.TaskManagement.Task> Tasks => Set<TaskFlow.Domain.TaskManagement.Task>();

    public DbSet<TaskFlow.Domain.TaskManagement.Project> Projects => Set<TaskFlow.Domain.TaskManagement.Project>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
