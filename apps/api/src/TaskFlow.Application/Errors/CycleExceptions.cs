namespace TaskFlow.Application.Errors;

/// <summary>
/// Thrown when <c>ActivateCycle</c> targets a cycle that is not planned (active or closed).
/// Mapped to HTTP 422 with <c>errorCode = "cycle_not_planned"</c> (slice 011, contracts/cycles-api.md).
/// </summary>
public sealed class CycleNotPlannedException : Exception
{
    public CycleNotPlannedException()
        : base("Only a planned cycle can be activated.")
    {
    }

    public CycleNotPlannedException(string message)
        : base(message)
    {
    }

    public CycleNotPlannedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when <c>ActivateCycle</c> would violate the single-active invariant — another cycle is
/// already active (handler check; the <c>ix_cycles_single_active</c> partial unique index closes
/// the race, D3). Mapped to HTTP 409 with <c>errorCode = "cycle_active_conflict"</c>.
/// </summary>
public sealed class CycleActiveConflictException : Exception
{
    public CycleActiveConflictException()
        : base("Another cycle is already active; close it first.")
    {
    }

    public CycleActiveConflictException(string message)
        : base(message)
    {
    }

    public CycleActiveConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when <c>CloseCycle</c> targets a cycle that is not active (planned or closed).
/// Mapped to HTTP 422 with <c>errorCode = "cycle_not_active"</c>.
/// </summary>
public sealed class CycleNotActiveException : Exception
{
    public CycleNotActiveException()
        : base("Only an active cycle can be closed.")
    {
    }

    public CycleNotActiveException(string message)
        : base(message)
    {
    }

    public CycleNotActiveException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when <c>DeleteCycle</c> targets a planned/closed cycle that still has assigned tasks
/// (FR-020; the <c>tasks.cycle_id</c> FK RESTRICT is the DB backstop, D1). Mapped to HTTP 422
/// with <c>errorCode = "cycle_not_empty"</c>.
/// </summary>
public sealed class CycleNotEmptyException : Exception
{
    public CycleNotEmptyException()
        : base("The cycle still has assigned tasks; move or unassign them first.")
    {
    }

    public CycleNotEmptyException(string message)
        : base(message)
    {
    }

    public CycleNotEmptyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when <c>DeleteCycle</c> targets the ACTIVE cycle (EC-04/FR-019: it must be closed
/// first). Mapped to HTTP 422 with <c>errorCode = "cycle_active_delete_forbidden"</c>.
/// </summary>
public sealed class CycleActiveDeleteForbiddenException : Exception
{
    public CycleActiveDeleteForbiddenException()
        : base("An active cycle cannot be deleted; close it first.")
    {
    }

    public CycleActiveDeleteForbiddenException(string message)
        : base(message)
    {
    }

    public CycleActiveDeleteForbiddenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when <c>CloseCycle</c> is asked to roll incomplete tasks to the next cycle but no
/// planned cycle exists (US-05.AS-06 — the UI prompts to create one first). Mapped to HTTP 422
/// with <c>errorCode = "no_next_cycle"</c>.
/// </summary>
public sealed class NoNextCycleException : Exception
{
    public NoNextCycleException()
        : base("No planned cycle exists to roll tasks into; create one first.")
    {
    }

    public NoNextCycleException(string message)
        : base(message)
    {
    }

    public NoNextCycleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Translation of the Postgres unique-violation on the <c>cycles</c> PK — a concurrent
/// double-insert of the same client-generated id. The <c>CreateCycle</c> handler catches this and
/// re-resolves the race to the idempotent 200 (the slice-002 <c>DuplicateTaskIdException</c>
/// pattern); it never reaches the wire on the create path.
/// </summary>
public sealed class DuplicateCycleIdException : Exception
{
    public DuplicateCycleIdException()
        : base("A cycle with this id already exists.")
    {
    }

    public DuplicateCycleIdException(string message)
        : base(message)
    {
    }

    public DuplicateCycleIdException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
