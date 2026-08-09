using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;
using CommentEntity = TaskFlow.Domain.TaskManagement.Comment;
using CommentId = TaskFlow.Domain.TaskManagement.CommentId;
using DomainProject = TaskFlow.Domain.TaskManagement.Project;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;
using ProjectId = TaskFlow.Domain.TaskManagement.ProjectId;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.IntegrationTests.Comments;

/// <summary>
/// Shared fixture helpers for the slice-009 comment suites (the <see cref="SharingTestBase"/> extension):
/// the canonical owner/editor/viewer shared-project + task scenario, HTTP post/edit/delete helpers, direct
/// comment-row seeding (for the reaper cases, mirroring the slice-DeleteTask seeding posture), and the
/// tombstone-inclusive row/mention loads the soft-delete assertions need.
/// </summary>
public abstract class CommentsTestBase : SharingTestBase
{
    protected static string CommentsPath(Guid taskId) => $"/api/tasks/{taskId}/comments";

    protected static string CommentPath(Guid commentId) => $"/api/comments/{commentId}";

    /// <summary>The canonical slice-009 scenario: a shared project with owner O, editor E, viewer V and one task.</summary>
    protected sealed record CommentScenario(UserId Owner, UserId Editor, UserId Viewer, ProjectBody Project, Guid TaskId);

    /// <summary>
    /// Admits O/E/V, creates + shares a project owned by O, seeds E (editor) + V (viewer) membership rows,
    /// and seeds one task under the project. <paramref name="slug"/> keeps the seeded identities unique per test.
    /// </summary>
    protected async Task<CommentScenario> CreateSharedScenarioAsync(string slug)
    {
        var owner = await CreateUserAsync($"g-{slug}-o", $"{slug}-o@example.com", "Owner O");
        var editor = await CreateUserAsync($"g-{slug}-e", $"{slug}-e@example.com", "Editor E");
        var viewer = await CreateUserAsync($"g-{slug}-v", $"{slug}-v@example.com", "Viewer V");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);
        var taskId = await SeedTaskUnderProjectAsync(owner, project.Id, "Discussed task", "a0");
        return new CommentScenario(owner, editor, viewer, project, taskId);
    }

    /// <summary>Posts a comment through the real HTTP surface and asserts 200 (the allow-path helper).</summary>
    protected async Task<CommentBody> PostCommentAsync(string token, Guid taskId, string body, params Guid[] mentionedUserIds)
    {
        using var response = await SendAsync(
            HttpMethod.Post, CommentsPath(taskId), token, new { body, mentionedUserIds });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the helper posts a valid comment");
        return await response.ReadCommentAsync();
    }

    /// <summary>Loads the persisted comment row tombstone-INCLUSIVE (with its owned mention set), or null.</summary>
    protected async Task<CommentEntity?> LoadCommentRowAsync(Guid id)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Comments.FirstOrDefaultAsync(c => c.Id == CommentId.From(id));
    }

    /// <summary>Counts the live comment rows of a task (tombstones included = false) — the "no thread entry" assertions.</summary>
    protected async Task<int> CountCommentRowsAsync(Guid taskId, bool includeDeleted = false)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var query = db.Comments.Where(c => c.TaskId == TaskId.From(taskId));
        if (!includeDeleted)
        {
            query = query.Where(c => c.DeletedAt == null);
        }

        return await query.CountAsync();
    }

    /// <summary>
    /// Seeds a comment row DIRECTLY through the DbContext (the reaper/tombstone cases must not depend on the
    /// HTTP delete), optionally already soft-deleted. Returns the (id, deletedAt) pair — <c>deletedAt</c> is
    /// the scheduled <c>ReapDeletedComment.DeletedAtInstant</c> the restore-aware reaper matches.
    /// </summary>
    protected async Task<(Guid Id, DateTime? DeletedAt)> SeedCommentAsync(
        Guid taskId, Guid projectId, UserId author, string body, bool softDeleted = false, params UserId[] mentions)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var comment = CommentEntity.Post(
            TaskId.From(taskId), ProjectId.From(projectId), author, body, mentions, DateTime.UtcNow);
        if (softDeleted)
        {
            comment.SoftDelete(DateTime.UtcNow);
        }

        comment.ClearDomainEvents();
        db.Comments.Add(comment);
        await db.SaveChangesAsync();
        return (comment.Id.Value, comment.DeletedAt);
    }

    /// <summary>Removes a membership row directly (the former-member cases — membership loss revokes ALL, FR-066).</summary>
    protected async Task RemoveMembershipRowAsync(Guid projectId, UserId userId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.ProjectMemberships
            .SingleAsync(m => m.ProjectId == ProjectId.From(projectId) && m.UserId == userId);
        db.ProjectMemberships.Remove(row);
        await db.SaveChangesAsync();
    }

    /// <summary>Demotes a member's role in place (the H1 demoted-author cases — the role floor beats the author grant).</summary>
    protected async Task ChangeMembershipRoleRowAsync(Guid projectId, UserId userId, string role)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.ProjectMemberships
            .SingleAsync(m => m.ProjectId == ProjectId.From(projectId) && m.UserId == userId);
        db.Entry(row).Property(nameof(TaskFlow.Domain.TaskManagement.ProjectMembership.Role)).CurrentValue = role;
        await db.SaveChangesAsync();
    }

    /// <summary>Flips a shared project back to personal visibility directly (the now-personal parent → 404 cases).</summary>
    protected async Task MakeProjectPersonalAsync(Guid projectId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Projects.IgnoreQueryFilters().SingleAsync(p => p.Id == ProjectId.From(projectId));
        db.Entry(row).Property(nameof(DomainProject.Visibility)).CurrentValue = DomainProject.PersonalVisibility;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Counts the durable, still-Scheduled <c>ReapDeletedComment</c> envelopes referencing
    /// <paramref name="commentId"/> — the DeleteTaskTests durable-storage assertion mechanism: a +30s
    /// ScheduleDelay parks the publish in <c>wolverine.wolverine_incoming_envelopes</c> (status 'Scheduled'),
    /// where an in-process tracking session cannot capture it without waiting out the delay.
    /// </summary>
    protected async Task<int> CountScheduledReapEnvelopesAsync(Guid commentId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            using var command = connection.CreateCommand();
            // encode(body,'escape') renders the printable-ASCII JSON tail of the envelope (where the
            // serialized CommentId GUID lives) as text so we can substring-match it.
            command.CommandText =
                "SELECT count(*) FROM wolverine.wolverine_incoming_envelopes " +
                "WHERE status = 'Scheduled' " +
                "AND message_type = @messageType " +
                "AND encode(body, 'escape') LIKE '%' || @commentId || '%'";

            var messageType = command.CreateParameter();
            messageType.ParameterName = "messageType";
            messageType.Value = typeof(TaskFlow.Domain.TaskManagement.Events.ReapDeletedComment).FullName;
            command.Parameters.Add(messageType);

            var idParam = command.CreateParameter();
            idParam.ParameterName = "commentId";
            idParam.Value = commentId.ToString();
            command.Parameters.Add(idParam);

            return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    /// <summary>Counts the persisted <c>comment_mentions</c> rows of a comment (the reaper-cascade assertion).</summary>
    protected async Task<int> CountMentionRowsAsync(Guid commentId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM comment_mentions WHERE comment_id = @commentId";
            var idParam = command.CreateParameter();
            idParam.ParameterName = "commentId";
            idParam.Value = commentId;
            command.Parameters.Add(idParam);
            return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}
