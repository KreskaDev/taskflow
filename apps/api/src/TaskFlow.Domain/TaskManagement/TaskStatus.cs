namespace TaskFlow.Domain.TaskManagement;

/// <summary>
/// Lifecycle status of a <c>Task</c> (FR-003). Persisted as lowercase snake_case
/// text (<c>backlog | todo | in_progress | done | cancelled</c>) via the EF Core
/// <c>HasConversion&lt;string&gt;()</c> mapping in <c>TaskConfiguration</c> — the
/// member names stay PascalCase here and the wire/db string is produced by the
/// conversion, never encoded into the names.
/// </summary>
/// <remarks>
/// Only <see cref="Backlog"/> and <see cref="Done"/> are reachable in slice 002
/// (the <c>Space</c> toggle); <see cref="Todo"/>, <see cref="InProgress"/>, and
/// <see cref="Cancelled"/> are storable but unused this slice.
/// </remarks>
public enum TaskStatus
{
    Backlog,
    Todo,
    InProgress,
    Done,
    Cancelled,
}
