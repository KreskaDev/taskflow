namespace TaskFlow.Domain.TaskManagement;

/// <summary>
/// Lifecycle status of a <see cref="Cycle"/> (ENT-03, slice 011). Stored as lowercase text
/// (<c>planned | active | closed</c>) like <see cref="TaskStatus"/>. Transitions are MANUAL only
/// (planned → active via <see cref="Cycle.Activate"/>, active → closed via
/// <see cref="Cycle.Close"/>); the server never moves a cycle by clock (Clarifications 2026-08-16).
/// </summary>
public enum CycleStatus
{
    /// <summary>Created, not yet activated; the rollover "next cycle" candidate set (D5).</summary>
    Planned,

    /// <summary>The single active cycle (at most one team-wide — the D3 partial unique index).</summary>
    Active,

    /// <summary>Manually closed; keeps its assignments as the historical record.</summary>
    Closed,
}
