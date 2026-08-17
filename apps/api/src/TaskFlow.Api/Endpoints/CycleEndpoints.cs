using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.TaskManagement;
using TaskFlow.Application.TaskManagement.Cycles;
using Wolverine;
using Wolverine.Http;
using CycleId = TaskFlow.Domain.TaskManagement.CycleId;

namespace TaskFlow.Api.Endpoints;

/// <summary>
/// HTTP surface for the Cycle aggregate (slice 011, contracts/cycles-api.md). Each endpoint is a
/// thin transport adapter dispatching through Wolverine's local message pipeline via
/// <see cref="IMessageBus.InvokeAsync{T}"/>, so the deny-by-default authorization middleware and
/// the FluentValidation boundary are woven ahead of every handler. Lifecycle/read operations are
/// TEAM-WIDE (any authenticated, admitted user — spec IX).
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine.Http discovers and maps HTTP endpoints only on public types; this class must stay public.")]
public static class CycleEndpoints
{
    /// <summary>
    /// Idempotent insert-if-not-exists of a cycle, keyed on the client-generated id in the route.
    /// Returns 200 on both first insert and replay. Name/dates validated (start &lt; end; overlap
    /// between cycles is allowed).
    /// </summary>
    [WolverinePut("/api/cycles/{id}")]
    public static Task<CycleResponse> Create(Guid id, CreateCycleRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<CycleResponse>(new CreateCycle
        {
            Id = CycleId.From(id),
            Name = request.Name,
            // A missing wire date maps to default(DateTime) so the validator rejects it as 422.
            StartDate = request.StartDate ?? default,
            EndDate = request.EndDate ?? default,
        });
    }

    /// <summary>List ALL cycles with computed team-wide metrics, in (startDate, createdAt, id) order (D5/D6).</summary>
    [WolverineGet("/api/cycles")]
    public static Task<IReadOnlyList<CycleResponse>> List(IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<IReadOnlyList<CycleResponse>>(new GetCycles());
    }

    /// <summary>
    /// A cycle's task rows filtered to the CALLER's visibility (D10/FR-065); archived-project
    /// tasks included (EC-12). 404 on an unknown cycle. Serves the Cycle view + the close review.
    /// </summary>
    [WolverineGet("/api/cycles/{id}/tasks")]
    public static Task<IReadOnlyList<TaskResponse>> Tasks(Guid id, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<IReadOnlyList<TaskResponse>>(new GetCycleTasks { Id = CycleId.From(id) });
    }

    /// <summary>
    /// Edit a cycle's name/dates under the optimistic <c>version</c> guard — legal in EVERY
    /// status (FR-020); start &lt; end re-validated; 404 unknown id; 409 stale version.
    /// </summary>
    [WolverinePatch("/api/cycles/{id}")]
    public static Task<CycleResponse> Edit(Guid id, EditCycleRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<CycleResponse>(new EditCycle
        {
            Id = CycleId.From(id),
            Name = request.Name,
            StartDate = request.StartDate ?? default,
            EndDate = request.EndDate ?? default,
            Version = request.Version,
        });
    }

    /// <summary>
    /// The manual planned → active transition (D3). Non-planned → 422 <c>cycle_not_planned</c>;
    /// another active cycle → 409 <c>cycle_active_conflict</c> (the partial unique index wins
    /// races); stale <c>version</c> → 409.
    /// </summary>
    [WolverinePatch("/api/cycles/{id}/activate")]
    public static Task<CycleResponse> Activate(Guid id, VersionOnlyRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<CycleResponse>(new ActivateCycle
        {
            Id = CycleId.From(id),
            Version = request.Version,
        });
    }

    /// <summary>
    /// The close review commit (D4): the active → closed flip + the FR-018 rollover of every
    /// incomplete task in ONE transaction; the response carries the counts. Non-active → 422
    /// <c>cycle_not_active</c>; rollover "next" with no planned cycle → 422 <c>no_next_cycle</c>;
    /// an override on an invisible task → 404 (whole command rejected).
    /// </summary>
    [WolverinePatch("/api/cycles/{id}/close")]
    public static Task<CloseCycleResponse> Close(Guid id, CloseCycleRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<CloseCycleResponse>(new CloseCycle
        {
            Id = CycleId.From(id),
            Rollover = request.Rollover,
            Overrides = request.Overrides,
            Version = request.Version,
        });
    }

    /// <summary>
    /// Hard-delete a cycle behind the guards (EC-04/FR-019/FR-020): active → 422
    /// <c>cycle_active_delete_forbidden</c>; non-empty planned/closed → 422 <c>cycle_not_empty</c>
    /// (FK RESTRICT backstop); otherwise 204. OCC via the <c>version</c> query parameter.
    /// </summary>
    [WolverineDelete("/api/cycles/{id}")]
    public static Task Delete(Guid id, int version, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync(new DeleteCycle { Id = CycleId.From(id), Version = version });
    }
}
