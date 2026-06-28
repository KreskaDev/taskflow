using TaskFlow.Domain.TaskManagement;
using Wolverine;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Drains the <see cref="Project"/> aggregate's recorded <c>DomainEvents</c> onto the Wolverine
/// transactional outbox and clears them (research R13). Called by the sharing/transfer handlers AFTER the
/// behavior method records its event and BEFORE <c>SaveChangesAsync</c>, so the publish enrolls in the
/// same per-message transaction as the EF write (atomic publish + commit, mirroring <c>DeleteAccount</c>).
/// </summary>
/// <remarks>
/// Wolverine resolves the publish route from each event's <b>runtime</b> type, so publishing through the
/// <c>DomainEvent</c> base reference still routes <see cref="Events.ProjectShared"/> /
/// <see cref="Events.ProjectUnshared"/> / <see cref="Events.OwnerTransferred"/> to the
/// <c>membership-events</c> local queue (verified by the <c>.Sent.MessagesOf&lt;T&gt;()</c> integration
/// assertions). <see cref="Events.MembershipRevoked"/> is NOT carried here — a <c>ProjectMembership</c> row
/// is an entity, not an aggregate root, so its handler publishes that event directly.
/// </remarks>
internal static class DomainEventDispatch
{
    public static async Task PublishAndClearAsync(Project project, IMessageContext messages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(messages);

        foreach (var domainEvent in project.DomainEvents)
        {
            await messages.PublishAsync(domainEvent).ConfigureAwait(false);
        }

        project.ClearDomainEvents();
    }
}
