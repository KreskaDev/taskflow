using TaskFlow.Domain.TaskManagement.Events;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// No-op consumer for <see cref="TaskAssigned"/> (slice 008). A registered handler + the local-queue route
/// in Program.cs give the event a durable outbox-backed destination so the publish is ROUTABLE (an unrouted
/// Wolverine publish is silently dropped) and observable through the tracking harness
/// (<c>.Sent.MessagesOf&lt;TaskAssigned&gt;()</c>). slice 008 RAISES the event but does NOT deliver
/// notifications — slice 017 (notifications) ships the real consumer (notify each added assignee, suppressing
/// the actor's own self-assignment via <c>ActorUserId</c>). Do NOT do real work here.
/// </summary>
public static class TaskAssignedHandler
{
    public static void Handle(TaskAssigned _)
    {
    }
}
