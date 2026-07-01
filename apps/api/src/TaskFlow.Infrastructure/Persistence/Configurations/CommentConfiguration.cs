using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="Comment"/> aggregate (ENT-08, data-model.md §6, slice 009) and its
/// owned <see cref="CommentMention"/> child collection (the <c>comment_mentions</c> table — mapped INLINE via
/// <c>OwnsMany</c>, mirroring the slice-008 <c>task_assignees</c> convention; there is NO separate
/// CommentMention config). All temporal columns are <c>timestamptz</c> (Constitution X). Comments are
/// <b>versionless</b> (no concurrency token — LWW, R2). The FK directions encode ownership vs privacy:
/// <c>task_id</c>/<c>comment_id</c> CASCADE (owned), <c>author_id</c>/<c>user_id</c> SET NULL (the tombstone
/// FKs — the author/mention survive account erasure as a neutral tombstone, R11). No DDL touches <c>tasks</c>.
/// </summary>
public sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("comments", t =>
            t.HasCheckConstraint("ck_comments_body_length", $"char_length(body) <= {Comment.MaxBodyLength}"));

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => CommentId.From(value))
            .ValueGeneratedNever();

        builder.Property(c => c.TaskId)
            .HasColumnName("task_id")
            .HasConversion(id => id.Value, value => TaskId.From(value))
            .IsRequired();

        // author_id → users(id) ON DELETE SET NULL (nullable): the one FK that breaks house-style cascade
        // parity — on account erasure the author is tombstoned and the comment SURVIVES where it anchors a
        // thread (Constitution XI/R11). The explicit nullable converter keeps EF from tripping on UserId? → uuid?.
        builder.Property(c => c.AuthorId)
            .HasColumnName("author_id")
            .HasConversion(
                id => id == null ? (Guid?)null : id.Value.Value,
                value => value == null ? (UserId?)null : UserId.From(value.Value));

        builder.Property(c => c.Body)
            .HasColumnName("body")
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(c => c.EditedAt)
            .HasColumnName("edited_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(c => c.DeletedAt)
            .HasColumnName("deleted_at")
            .HasColumnType("timestamp with time zone");

        // task_id → tasks(id) ON DELETE CASCADE: a comment is meaningless without its parent task; deleting
        // the task (hard-delete/reaper) erases its comments. No navigation property on Task (no-nav-prop style).
        builder.HasOne<TaskEntity>()
            .WithMany()
            .HasForeignKey(c => c.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        // author_id → users(id) ON DELETE SET NULL: erasure tombstones the author (NOT cascade), so the
        // comment is retained as an anonymized entry (R11). No navigation property.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.AuthorId)
            .OnDelete(DeleteBehavior.SetNull);

        // The thread read filters WHERE task_id = @task AND deleted_at IS NULL ORDER BY created_at (R5).
        builder.HasIndex(c => new { c.TaskId, c.CreatedAt })
            .HasDatabaseName("ix_comments_task_created");

        // (author_id): the erasure-cascade lookup (tombstone the author's comments) — R11.
        builder.HasIndex(c => c.AuthorId)
            .HasDatabaseName("ix_comments_author_id");

        // The typed @mention set (R6) — the comment_mentions child table, owned + replaced wholesale by the
        // aggregate mutators (mirrors task_assignees). Surrogate id PK (a PK column can't be SET NULL, R11/M2);
        // comment_id → comments CASCADE (owned default); user_id → users SET NULL (the residual tombstone).
        builder.OwnsMany(c => c.Mentions, m =>
        {
            m.ToTable("comment_mentions");
            m.WithOwner().HasForeignKey(x => x.CommentId);

            m.HasKey(x => x.Id);
            m.Property(x => x.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            m.Property(x => x.CommentId)
                .HasColumnName("comment_id")
                .HasConversion(id => id.Value, value => CommentId.From(value));

            m.Property(x => x.UserId)
                .HasColumnName("user_id")
                .HasConversion(
                    id => id == null ? (Guid?)null : id.Value.Value,
                    value => value == null ? (UserId?)null : UserId.From(value.Value));

            m.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            // UNIQUE (comment_id, user_id): de-dup the mention set. Postgres treats NULLs as distinct, so
            // erased-mention tombstones are harmless (multiple NULL user_ids coexist) — R11.
            m.HasIndex(x => new { x.CommentId, x.UserId })
                .HasDatabaseName("ux_comment_mentions_comment_user")
                .IsUnique();

            m.HasIndex(x => x.UserId)
                .HasDatabaseName("ix_comment_mentions_user_id");
        });

        // Domain events are an in-memory, transient concern — never persisted.
        builder.Ignore(c => c.DomainEvents);
    }
}
