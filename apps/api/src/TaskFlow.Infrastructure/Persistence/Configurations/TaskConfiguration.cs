using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskStatus = TaskFlow.Domain.TaskManagement.TaskStatus;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="TaskEntity"/> aggregate (ENT-01). All temporal columns are
/// <c>timestamptz</c> (Constitution X); <c>position</c> is collated <c>"C"</c> for byte-ordinal
/// sort (data-model.md). The seven reserved nullable columns are mapped but unused this slice.
/// </summary>
public sealed class TaskConfiguration : IEntityTypeConfiguration<TaskEntity>
{
    public void Configure(EntityTypeBuilder<TaskEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tasks");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => TaskId.From(value))
            .ValueGeneratedNever();

        builder.Property(t => t.CreatedBy)
            .HasColumnName("created_by")
            .HasConversion(id => id.Value, value => UserId.From(value))
            .IsRequired();

        builder.Property(t => t.Title)
            .HasColumnName("title")
            .IsRequired();

        // Stored as lowercase snake_case text (data-model.md: backlog | todo | in_progress |
        // done | cancelled), produced by an explicit two-way converter — the PascalCase enum
        // member names are never encoded into the wire/db string.
        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasConversion(
                status => ToDbStatus(status),
                value => FromDbStatus(value))
            .HasDefaultValueSql("'backlog'")
            .IsRequired();

        builder.Property(t => t.Position)
            .HasColumnName("position")
            .UseCollation("C")
            .IsRequired();

        builder.Property(t => t.Version)
            .HasColumnName("version")
            .HasDefaultValue(0)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(t => t.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(t => t.DeletedAt)
            .HasColumnName("deleted_at")
            .HasColumnType("timestamp with time zone");

        // Reserved forward-compatible columns (data-model.md): mapped, nullable, unused this
        // slice — kept so their owning slices need no schema migration.
        builder.Property(t => t.Description)
            .HasColumnName("description");

        builder.Property(t => t.Priority)
            .HasColumnName("priority");

        builder.Property(t => t.DueDate)
            .HasColumnName("due_date")
            .HasColumnType("timestamp with time zone");

        builder.Property(t => t.DueHasTime)
            .HasColumnName("due_has_time");

        // Strongly-typed nullable id (slice 004): the explicit nullable converter keeps EF from
        // tripping on the ProjectId? → uuid? round-trip (null = Inbox). The store column is unchanged
        // (uuid NULL) — activating the CLR type is not a column change.
        builder.Property(t => t.ProjectId)
            .HasColumnName("project_id")
            .HasConversion(
                id => id == null ? (Guid?)null : id.Value.Value,
                value => value == null ? (ProjectId?)null : ProjectId.From(value.Value));

        builder.Property(t => t.CycleId)
            .HasColumnName("cycle_id");

        builder.Property(t => t.RecurrenceRule)
            .HasColumnName("recurrence_rule")
            .HasColumnType("jsonb");

        // created_by → users(id), ON DELETE CASCADE (NOT the EF default Restrict): slice-001's
        // hard-delete of the User row erases the user's personal tasks atomically (Constitution XI).
        // No navigation property (data-model.md: no nav props this slice).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.CreatedBy)
            .OnDelete(DeleteBehavior.Cascade);

        // project_id → projects(id), ON DELETE SET NULL (slice 004, R14): the column already exists
        // (reserved by slice-002 AddTasks); this slice adds only the FK constraint. SET NULL is the
        // defensive backstop under soft-delete — the delete dispositions (R5) reconcile a project's
        // tasks BEFORE the reaper hard-deletes, so this only ever catches a straggler. A null
        // project_id is the Inbox (FR-021/R6). No navigation property (data-model.md: no nav props).
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(t => t.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);

        // Partial composite index serving the single hot query exactly:
        // WHERE created_by = @caller AND deleted_at IS NULL ORDER BY position, id.
        // position is collated "C" at the column level, which the index inherits; the partial
        // filter (snake_case raw SQL) keeps tombstones out of the index. NO unique constraint.
        builder.HasIndex(t => new { t.CreatedBy, t.Position })
            .HasDatabaseName("ix_tasks_created_by_position")
            .HasFilter("deleted_at IS NULL");

        // Assignees (slice 008) — the owned collection mapped to the task_assignees join table. Composite
        // PK (task_id, user_id) enforces set-uniqueness. The owner FK (task_id → tasks) cascades with the
        // task (hard-delete/reaper cleanup). The user_id → users(id) FK cascades on account deletion (the
        // erasure cascade, Constitution XI/FR-085 — mirrors created_by). Assignment is shared-only (FR-069),
        // enforced at the handler; the schema does not constrain visibility.
        builder.OwnsMany(t => t.Assignees, a =>
        {
            a.ToTable("task_assignees");
            a.WithOwner().HasForeignKey("TaskId");
            a.Property<TaskId>("TaskId")
                .HasColumnName("task_id")
                .HasConversion(id => id.Value, value => TaskId.From(value));
            a.Property(x => x.UserId)
                .HasColumnName("user_id")
                .HasConversion(id => id.Value, value => UserId.From(value));
            a.HasKey("TaskId", "UserId");
            a.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            a.HasIndex(x => x.UserId).HasDatabaseName("ix_task_assignees_user_id"); // snake_case index convention
        });

        // Domain events are an in-memory, transient concern — never persisted.
        builder.Ignore(t => t.DomainEvents);
    }

    private static string ToDbStatus(TaskStatus status) => status switch
    {
        TaskStatus.Backlog => "backlog",
        TaskStatus.Todo => "todo",
        TaskStatus.InProgress => "in_progress",
        TaskStatus.Done => "done",
        TaskStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown task status."),
    };

    private static TaskStatus FromDbStatus(string value) => value switch
    {
        "backlog" => TaskStatus.Backlog,
        "todo" => TaskStatus.Todo,
        "in_progress" => TaskStatus.InProgress,
        "done" => TaskStatus.Done,
        "cancelled" => TaskStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown task status."),
    };
}
