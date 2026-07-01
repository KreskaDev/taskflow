using TaskFlow.Domain.TaskManagement.Events;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// No-op consumer for <see cref="UserMentioned"/> (slice 009, R7). A registered handler + the local-queue
/// route in Program.cs give the event a durable outbox-backed destination so the publish is ROUTABLE (an
/// unrouted Wolverine publish is silently dropped) and observable through the in-process tracking harness
/// (the integration tests assert <c>.Sent.MessagesOf&lt;UserMentioned&gt;()</c>). Slice 017 (notifications)
/// replaces this with the REAL consumer that notifies each mentioned member — additively, with no change to
/// this slice (the <c>TaskAssignedHandler</c> / <c>ProjectSharedHandler</c> precedent).
/// </summary>
public static class UserMentionedHandler
{
    public static void Handle(UserMentioned _)
    {
    }
}
