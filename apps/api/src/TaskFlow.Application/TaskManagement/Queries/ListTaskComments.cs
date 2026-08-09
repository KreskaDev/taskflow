using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.IdentityAccess;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Application.TaskManagement.Queries;

/// <summary>
/// Lists a shared-project task's live comment thread (contracts/openapi.yaml <c>listTaskComments</c>,
/// AS-01/AS-03, research R3/R5/R15): chronological by <c>created_at</c>, soft-deleted rows excluded, each
/// item carrying tombstone-safe author identity, the typed mention tokens, and the caller-scoped
/// <c>canEdit</c>. Readable by ANY current member (viewer+); a non-member / former member / personal task /
/// foreign task → 404 (no existence leak). NEVER echoes an email (Constitution XI).
/// </summary>
public sealed record ListTaskComments
{
    /// <summary>The parent task identity, carried in the route.</summary>
    public required TaskId TaskId { get; init; }
}

/// <summary>Handles <see cref="ListTaskComments"/> (the viewer+ member-only thread read).</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-007 GetProjectMembersHandler).")]
public static class ListTaskCommentsHandler
{
    public static async Task<CommentListResponse> Handle(
        ListTaskComments query,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        IResourceAuthorizationPolicy authorization,
        ICommentRepository comments,
        IUserRepository users,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(comments);
        ArgumentNullException.ThrowIfNull(users);

        // Any current member (viewer+) reads the thread; every deny shape (non-member, former member,
        // personal/Inbox task, foreign/absent task) 404s inside the reused guard (R3).
        await CommentAccessGuards
            .LoadForCommentAsync(query.TaskId, EffectiveRole.Viewer, currentUser, tasks, projects, members, authorization, cancellationToken)
            .ConfigureAwait(false);

        var thread = await comments.ListByTaskAsync(query.TaskId, cancellationToken).ConfigureAwait(false);
        var displayNames = await CommentMentionRules
            .ResolveDisplayNamesAsync(thread, users, cancellationToken)
            .ConfigureAwait(false);

        return new CommentListResponse
        {
            TaskId = query.TaskId.Value,
            Comments = thread
                .Select(c => CommentResponse.From(c, currentUser.Id, displayNames))
                .ToList(),
        };
    }
}
