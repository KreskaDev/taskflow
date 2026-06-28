using TaskFlow.Domain.TaskManagement.Events;

namespace TaskFlow.Application.TaskManagement;

// No-op consumers for the slice-007 membership/sharing domain events. As with
// AccountDeletionRequestedHandler (slice 001), a registered handler plus the local-queue routes in
// Program.cs give each event a durable outbox-backed destination so the publish is ROUTABLE (an
// unrouted Wolverine publish is silently dropped) and observable through the in-process tracking
// harness (the integration tests assert `.Sent.MessagesOf<T>()`). This slice RAISES these events but
// does NOT consume them: the real consumers ship with later slices — slice 008 clears the affected
// user's task assignments, slice 016 evicts their live subscriptions, slice 017 notifies (research
// R13/R14). Do NOT do real work here.

/// <summary>No-op consumer for <see cref="ProjectShared"/> (the authority signal; consumers = slices 016/017).</summary>
public static class ProjectSharedHandler
{
    public static void Handle(ProjectShared _)
    {
    }
}

/// <summary>No-op consumer for <see cref="ProjectUnshared"/> (revoke-all authority; consumers = slices 008/016/017).</summary>
public static class ProjectUnsharedHandler
{
    public static void Handle(ProjectUnshared _)
    {
    }
}

/// <summary>No-op consumer for <see cref="OwnerTransferred"/> (consumers = slices 016/017).</summary>
public static class OwnerTransferredHandler
{
    public static void Handle(OwnerTransferred _)
    {
    }
}

/// <summary>No-op consumer for <see cref="MembershipRevoked"/> (revoke/demote authority; consumers = slices 008/016/017).</summary>
public static class MembershipRevokedHandler
{
    public static void Handle(MembershipRevoked _)
    {
    }
}
