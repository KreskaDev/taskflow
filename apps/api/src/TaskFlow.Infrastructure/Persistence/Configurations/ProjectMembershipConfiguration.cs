using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="ProjectMembership"/> entity (ENT-07, data-model.md §1/§6). The
/// table is owned by the <see cref="Project"/> aggregate but mapped as a standalone table with NO
/// navigation collection on <see cref="Project"/> (the slice-004 no-nav-prop style, R1). Both FKs cascade
/// (a membership row is meaningless without its project, and erasure parity with <c>owner_id</c>/
/// <c>created_by</c> on user deletion, Constitution XI); the <c>role</c> column is constrained to the
/// stored vocabulary (<c>editor | viewer</c>, R2). No DDL touches the <c>projects</c> table.
/// </summary>
public sealed class ProjectMembershipConfiguration : IEntityTypeConfiguration<ProjectMembership>
{
    public void Configure(EntityTypeBuilder<ProjectMembership> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("project_memberships", t =>
            t.HasCheckConstraint("ck_project_memberships_role", "role IN ('editor', 'viewer')"));

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => ProjectMembershipId.From(value))
            .ValueGeneratedNever();

        builder.Property(m => m.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(id => id.Value, value => ProjectId.From(value))
            .IsRequired();

        builder.Property(m => m.UserId)
            .HasColumnName("user_id")
            .HasConversion(id => id.Value, value => UserId.From(value))
            .IsRequired();

        builder.Property(m => m.Role)
            .HasColumnName("role")
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(m => m.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // project_id → projects(id) ON DELETE CASCADE: the membership rows are the project's sharing state
        // (R1), so removing the project removes them. No navigation property (data-model: no nav props).
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // user_id → users(id) ON DELETE CASCADE: account erasure removes the user's memberships atomically
        // (Constitution XI; parity with owner_id / created_by).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // UNIQUE (project_id, user_id): at most one membership per user per project (R15). Its project_id
        // prefix also serves "list a project's members" and the per-request authorization lookup — so NO
        // standalone (project_id) index (self-review L1).
        builder.HasIndex(m => new { m.ProjectId, m.UserId })
            .HasDatabaseName("ux_project_memberships_project_user")
            .IsUnique();

        // (user_id): "shared projects I belong to" (the sidebar) + the user-erasure cascade lookup (R15).
        builder.HasIndex(m => m.UserId)
            .HasDatabaseName("ix_project_memberships_user_id");
    }
}
