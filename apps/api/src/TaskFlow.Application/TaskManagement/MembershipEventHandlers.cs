using TaskFlow.Domain.TaskManagement.Events;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Application.TaskManagement;

// Consumers for the slice-007 membership/sharing domain events. A registered handler + the local-queue
// routes in Program.cs give each event a durable outbox-backed destination so the publish is ROUTABLE (an
// unrouted Wolverine publish is silently dropped) and observable through the in-process tracking harness
// (the integration tests assert `.Sent.MessagesOf<T>()`). These run OFF the durable queue (no HttpContext),
// so they are excluded from AuthorizationMiddleware in Program.cs. slice 008 turns ProjectUnshared /
// MembershipRevoked into REAL consumers that clear now-illegal task assignments (research R5, the slice-007
// MembershipEventHandlers TODO); ProjectShared / OwnerTransferred stay no-op (consumers = slices 016/017).

/// <summary>No-op consumer for <see cref="ProjectShared"/> (the authority signal; consumers = slices 016/017).</summary>
public static class ProjectSharedHandler
{
    public static void Handle(ProjectShared _)
    {
    }
}

/// <summary>
/// Consumes <see cref="ProjectUnshared"/> (slice 008, R5): the project reverted to personal, so assignment no
/// longer applies — clear ALL its tasks' assignees. The tasks keep their <c>project_id</c> (unshare does not
/// move tasks), so the bulk clear by project finds them. Runs in the per-message transaction.
/// </summary>
public static class ProjectUnsharedHandler
{
    public static Task Handle(ProjectUnshared message, ITaskRepository tasks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(tasks);
        return tasks.ClearAssigneesForProjectAsync(message.ProjectId, cancellationToken);
    }
}

/// <summary>No-op consumer for <see cref="OwnerTransferred"/> (consumers = slices 016/017).</summary>
public static class OwnerTransferredHandler
{
    public static void Handle(OwnerTransferred _)
    {
    }
}

/// <summary>
/// Consumes <see cref="MembershipRevoked"/> (slice 008, R5). The event is raised by remove / leave AND a
/// role DEMOTION (editor → viewer). A demoted user is STILL a member (a viewer may remain an assignee), so
/// the handler clears the user's assignments ONLY when they are no longer a member of the project — checked
/// by a membership lookup, making it correct regardless of the raiser. Runs in the per-message transaction.
/// </summary>
public static class MembershipRevokedHandler
{
    public static async Task Handle(
        MembershipRevoked message,
        IProjectMembershipRepository members,
        ITaskRepository tasks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(tasks);

        // Still a member (a demotion editor→viewer) → keep the assignments (a viewer is a valid member).
        var stillMember = await members.FindAsync(message.ProjectId, message.UserId, cancellationToken).ConfigureAwait(false);
        if (stillMember is not null)
        {
            return;
        }

        await tasks.ClearAssigneesForUserInProjectAsync(message.ProjectId, message.UserId, cancellationToken).ConfigureAwait(false);
    }
}
