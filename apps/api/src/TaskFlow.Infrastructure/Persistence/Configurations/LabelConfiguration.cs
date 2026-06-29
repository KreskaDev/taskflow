using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="Label"/> aggregate (ENT-04, data-model §1/§3). A per-user entity
/// (Tier A). Case-insensitive per-owner uniqueness is enforced by a <b>plain</b> unique index over the
/// persisted <c>name_normalized</c> column (EF Core 9 cannot model a functional <c>lower(name)</c> index,
/// R7), so the index is fully EF-generated and the snapshot round-trips. The <c>owner_id</c> FK cascades
/// (account-erasure parity, Constitution XI). No <c>version</c> column (single-owner, no concurrent edit).
/// </summary>
public sealed class LabelConfiguration : IEntityTypeConfiguration<Label>
{
    public void Configure(EntityTypeBuilder<Label> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("labels");

        // The AggregateRoot<T> domain-event collection is not persisted (drained to the outbox by the
        // handler) — mirror the Task/Project/User configs which Ignore it, else EF maps DomainEvent (no key).
        builder.Ignore(l => l.DomainEvents);

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => LabelId.From(value))
            .ValueGeneratedNever();

        builder.Property(l => l.OwnerId)
            .HasColumnName("owner_id")
            .HasConversion(id => id.Value, value => UserId.From(value))
            .IsRequired();

        builder.Property(l => l.Name)
            .HasColumnName("name")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.NameNormalized)
            .HasColumnName("name_normalized")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.Color)
            .HasColumnName("color");

        builder.Property(l => l.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(l => l.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // UNIQUE (owner_id, name_normalized): one label per (owner, case-folded name) — the per-owner
        // case-insensitive uniqueness backstop (R7). Plain index over the normalized column (NOT a functional
        // lower(name) index, which EF can't model). The owner_id prefix also serves the roster scope.
        builder.HasIndex(l => new { l.OwnerId, l.NameNormalized })
            .HasDatabaseName("ux_labels_owner_name")
            .IsUnique();

        // (owner_id): the "list my labels" roster scope + the user-erasure cascade lookup.
        builder.HasIndex(l => l.OwnerId)
            .HasDatabaseName("ix_labels_owner_id");

        // owner_id → users(id) ON DELETE CASCADE: account erasure removes the user's labels atomically
        // (Constitution XI / FR-085; parity with project owner_id / task created_by).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(l => l.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
