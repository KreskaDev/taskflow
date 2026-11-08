namespace TaskFlow.Domain.TaskManagement;

/// <summary>
/// Raised when a write would leave a <see cref="Task"/> with a recurrence rule but no due date
/// (slice 012, Clarifications Q4, research D9): setting a rule on a dateless task, or clearing the due
/// of a recurring task through the editor's whole-object save or a reschedule. Mapped by the API to
/// <c>422 recurrence_requires_due_date</c>; the rule is never dropped silently.
/// </summary>
public sealed class RecurrenceRequiresDueDateException : Exception
{
    public RecurrenceRequiresDueDateException()
        : base("A recurring task must keep a due date; disable recurrence first.")
    {
    }

    public RecurrenceRequiresDueDateException(string message)
        : base(message)
    {
    }

    public RecurrenceRequiresDueDateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
