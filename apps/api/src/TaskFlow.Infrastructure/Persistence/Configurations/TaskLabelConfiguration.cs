using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.TaskManagement;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="TaskLabel"/> join (the <c>task_labels</c> relation, data-model §1/§3).
/// A <b>hybrid</b> of two precedents: the composite key over value-converted strongly-typed ids follows the
/// slice-008 <c>task_assignees</c> block, and the standalone-entity / no-navigation-property shape follows
/// <see cref="ProjectMembershipConfiguration"/>. Both FKs cascade on delete: a task hard-delete (the reaper),
/// a label delete, and — transitively via <c>labels.owner_id</c> — account erasure all clean up the rows.
/// </summary>
public sealed class TaskLabelConfiguration : IEntityTypeConfiguration<TaskLabel>
{
    public void Configure(EntityTypeBuilder<TaskLabel> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("task_labels");

        // Composite PK (task_id, label_id): a label is applied to a task at most once (set-uniqueness).
        builder.HasKey(e => new { e.TaskId, e.LabelId });

        builder.Property(e => e.TaskId)
            .HasColumnName("task_id")
            .HasConversion(id => id.Value, value => TaskId.From(value));

        builder.Property(e => e.LabelId)
            .HasColumnName("label_id")
            .HasConversion(id => id.Value, value => LabelId.From(value));

        // task_id → tasks(id) ON DELETE CASCADE: a task hard-delete (the reaper) removes its applications.
        builder.HasOne<TaskEntity>()
            .WithMany()
            .HasForeignKey(e => e.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        // label_id → labels(id) ON DELETE CASCADE: deleting a label removes its applications (and, via
        // labels.owner_id cascade, account erasure does too). No navigation property on either aggregate.
        builder.HasOne<Label>()
            .WithMany()
            .HasForeignKey(e => e.LabelId)
            .OnDelete(DeleteBehavior.Cascade);

        // (label_id): the reverse lookup + the FK. The composite PK's task_id prefix serves the per-task
        // lookup, so no standalone (task_id) index.
        builder.HasIndex(e => e.LabelId)
            .HasDatabaseName("ix_task_labels_label_id");
    }
}
