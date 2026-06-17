using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="User"/> aggregate (ENT-06) and the seeded
/// tombstone identity. All temporal columns are <c>timestamptz</c> (Constitution X).
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => UserId.From(value))
            .ValueGeneratedNever();

        builder.Property(u => u.GoogleSubjectId)
            .HasColumnName("google_subject_id")
            .IsRequired();

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .IsRequired();

        builder.Property(u => u.DisplayName)
            .HasColumnName("display_name")
            .IsRequired();

        builder.Property(u => u.AvatarUrl)
            .HasColumnName("avatar_url");

        builder.Property(u => u.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(u => u.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(u => u.GoogleSubjectId)
            .HasDatabaseName("ix_users_google_subject_id")
            .IsUnique();

        builder.HasIndex(u => u.Email)
            .HasDatabaseName("ix_users_email")
            .IsUnique();

        // Domain events are an in-memory, transient concern — never persisted.
        builder.Ignore(u => u.DomainEvents);

        // Tombstone identity (data-model.md): a well-known seeded row that later slices'
        // erasure-cascade handlers reattribute createdBy/assignee/comment-author references
        // to on account deletion. Sentinels use reserved values (RFC 2606 ".invalid" TLD;
        // a non-numeric "google_subject_id" Google never issues) so they cannot collide with
        // a real account. Timestamps are a fixed UTC literal so the seed is deterministic
        // (a non-deterministic seed re-scaffolds on every `migrations add`).
        builder.HasData(new
        {
            Id = UserId.Tombstone,
            GoogleSubjectId = "deleted-user",
            Email = "deleted-user@taskflow.invalid",
            DisplayName = "Deleted User",
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
    }
}
