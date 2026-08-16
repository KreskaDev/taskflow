using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Cycle = TaskFlow.Domain.TaskManagement.Cycle;
using CycleId = TaskFlow.Domain.TaskManagement.CycleId;
using CycleStatus = TaskFlow.Domain.TaskManagement.CycleStatus;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="Cycle"/> aggregate (ENT-03, slice 011 D1). Status stored as
/// lowercase text like <c>TaskStatus</c>; the single-active invariant is the
/// <c>ix_cycles_single_active</c> partial unique index (D3); <c>ix_cycles_ordering</c> serves the
/// D5 <c>(start_date, created_at, id)</c> ordering. All temporal columns are <c>timestamptz</c>.
/// </summary>
public sealed class CycleConfiguration : IEntityTypeConfiguration<Cycle>
{
    public void Configure(EntityTypeBuilder<Cycle> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("cycles");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => CycleId.From(value))
            .ValueGeneratedNever();

        builder.Property(c => c.Name)
            .HasColumnName("name")
            .IsRequired();

        builder.Property(c => c.StartDate)
            .HasColumnName("start_date")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(c => c.EndDate)
            .HasColumnName("end_date")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // Lowercase text (planned | active | closed) via an explicit two-way converter — the
        // PascalCase enum member names are never encoded into the db string (TaskStatus precedent).
        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasConversion(
                status => ToDbStatus(status),
                value => FromDbStatus(value))
            .HasDefaultValueSql("'planned'")
            .IsRequired();

        builder.Property(c => c.Version)
            .HasColumnName("version")
            .HasDefaultValue(0)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // The single-active invariant (D3): a UNIQUE index over the constant (1) restricted to
        // active rows — at most ONE row can satisfy the filter, so a racing second activation
        // loses at the database even if it slips past the handler check.
        builder.HasIndex(c => c.Status)
            .HasDatabaseName("ix_cycles_single_active")
            .IsUnique()
            .HasFilter("status = 'active'");

        // D5 ordering support: (start_date, created_at, id).
        builder.HasIndex(c => new { c.StartDate, c.CreatedAt, c.Id })
            .HasDatabaseName("ix_cycles_ordering");

        // Domain events are an in-memory, transient concern — never persisted.
        builder.Ignore(c => c.DomainEvents);
    }

    private static string ToDbStatus(CycleStatus status) => status switch
    {
        CycleStatus.Planned => "planned",
        CycleStatus.Active => "active",
        CycleStatus.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown cycle status."),
    };

    private static CycleStatus FromDbStatus(string value) => value switch
    {
        "planned" => CycleStatus.Planned,
        "active" => CycleStatus.Active,
        "closed" => CycleStatus.Closed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown cycle status."),
    };
}
