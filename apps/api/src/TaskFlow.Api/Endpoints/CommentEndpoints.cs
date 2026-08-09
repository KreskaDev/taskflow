using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.TaskManagement;
using TaskFlow.Application.TaskManagement.Queries;
using Wolverine;
using Wolverine.Http;
using CommentId = TaskFlow.Domain.TaskManagement.CommentId;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Api.Endpoints;

/// <summary>
/// HTTP surface for the <c>Comment</c> aggregate (slice 009, contracts/openapi.yaml). Each endpoint is a
/// thin transport adapter dispatching through Wolverine's local pipeline via
/// <see cref="IMessageBus.InvokeAsync{T}"/>, so the deny-by-default authorization middleware and the
/// FluentValidation boundary are woven ahead of every handler. Post/list are <c>taskId</c>-scoped (the
/// thread hangs on a task; the server mints the comment id under it); edit/delete are the flat
/// <c>commentId</c>-scoped shape (its own aggregate — research R1/L1).
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine.Http discovers and maps HTTP endpoints only on public types; this class must stay public.")]
public static class CommentEndpoints
{
    /// <summary>The live thread of a shared-project task, chronological (viewer+; non-member → 404).</summary>
    [WolverineGet("/api/tasks/{taskId}/comments")]
    public static Task<CommentListResponse> List(Guid taskId, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<CommentListResponse>(new ListTaskComments { TaskId = TaskId.From(taskId) });
    }

    /// <summary>
    /// Posts a comment on a shared-project task (editor+; viewer → 403). The server MINTS the
    /// <c>CommentId</c>; the author is resolved from <c>ICurrentUser</c> inside the handler — never the wire.
    /// </summary>
    [WolverinePost("/api/tasks/{taskId}/comments")]
    public static Task<CommentResponse> Post(Guid taskId, PostCommentRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<CommentResponse>(new PostComment
        {
            TaskId = TaskId.From(taskId),
            Body = request.Body,
            MentionedUserIds = request.MentionedUserIds ?? [],
        });
    }

    /// <summary>Author-only whole-body + whole-mention-set replace (the strict two-step gate, R4). LWW — no 409.</summary>
    [WolverinePatch("/api/comments/{commentId}")]
    public static Task<CommentResponse> Edit(Guid commentId, EditCommentRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<CommentResponse>(new EditComment
        {
            Id = CommentId.From(commentId),
            Body = request.Body,
            MentionedUserIds = request.MentionedUserIds ?? [],
        });
    }

    /// <summary>
    /// Author-only SOFT-delete (stamps <c>deleted_at</c> + schedules the 30s <c>ReapDeletedComment</c>
    /// reaper, R5). Returns 204 on success and on the idempotent replay of the caller's own tombstone.
    /// </summary>
    [WolverineDelete("/api/comments/{commentId}")]
    public static Task Delete(Guid commentId, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync(new DeleteComment { Id = CommentId.From(commentId) });
    }
}
