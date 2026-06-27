using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="Project"/> aggregate (ENT-02, data-model.md §1/§7). All
/// temporal columns are <c>timestamptz</c> (Constitution X); the owner FK cascades (erasure parity
/// with tasks, Constitution XI) and the self-referential parent FK is <c>SET NULL</c> (a defensive
/// backstop under soft-delete — dispositions reconcile children before the reaper, R5/R14).
/// </summary>
public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("projects");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => ProjectId.From(value))
            .ValueGeneratedNever();

        builder.Property(p => p.OwnerId)
            .HasColumnName("owner_id")
            .HasConversion(id => id.Value, value => UserId.From(value))
            .IsRequired();

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .IsRequired();

        builder.Property(p => p.Color)
            .HasColumnName("color")
            .IsRequired();

        builder.Property(p => p.Icon)
            .HasColumnName("icon")
            .IsRequired();

        // Nullable self-referential strongly-typed id: an explicit nullable converter keeps EF from
        // tripping on the ProjectId? → uuid? round-trip (null = top-level project).
        builder.Property(p => p.ParentId)
            .HasColumnName("parent_id")
            .HasConversion(
                id => id == null ? (Guid?)null : id.Value.Value,
                value => value == null ? (ProjectId?)null : ProjectId.From(value.Value));

        builder.Property(p => p.Visibility)
            .HasColumnName("visibility")
            .HasDefaultValueSql("'personal'")
            .IsRequired();

        builder.Property(p => p.ArchivedAt)
            .HasColumnName("archived_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(p => p.Version)
            .HasColumnName("version")
            .HasDefaultValue(0)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(p => p.DeletedAt)
            .HasColumnName("deleted_at")
            .HasColumnType("timestamp with time zone");

        // owner_id → users(id), ON DELETE CASCADE (NOT the EF default Restrict): slice-001's hard-delete
        // of the User row erases the user's personal projects atomically (Constitution XI; parity with
        // tasks.created_by). No navigation property (data-model.md: no nav props).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Self-referential parent_id → projects(id), ON DELETE SET NULL (R14): a defensive backstop —
        // the delete/archive child dispositions (R5) reconcile children BEFORE the reaper hard-deletes,
        // so this cascade-to-null only ever catches a straggler.
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(p => p.ParentId)
            .OnDelete(DeleteBehavior.SetNull);

        // Partial index serving the owner-scoped sidebar/archived queries exactly:
        // WHERE owner_id = @caller AND deleted_at IS NULL [AND archived_at IS / IS NOT NULL].
        // archived_at is filtered in-query (two disjoint sets, R8), not in the index.
        builder.HasIndex(p => p.OwnerId)
            .HasDatabaseName("ix_projects_owner_id")
            .HasFilter("deleted_at IS NULL");

        // Domain events are an in-memory, transient concern — never persisted.
        builder.Ignore(p => p.DomainEvents);
    }
}
