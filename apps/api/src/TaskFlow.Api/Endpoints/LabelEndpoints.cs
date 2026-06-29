using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.TaskManagement.Labels;
using Wolverine;
using Wolverine.Http;
using LabelId = TaskFlow.Domain.TaskManagement.LabelId;

namespace TaskFlow.Api.Endpoints;

/// <summary>
/// HTTP surface for the per-user <see cref="TaskFlow.Domain.TaskManagement.Label"/> aggregate
/// (contracts/openapi.yaml). Each endpoint is a thin transport adapter dispatching the command/query through
/// Wolverine's local message pipeline via <see cref="IMessageBus.InvokeAsync{T}"/>, so the deny-by-default
/// <c>AuthorizationMiddleware</c> and the FluentValidation boundary are woven ahead of every handler.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine.Http discovers and maps HTTP endpoints only on public types; this class must stay public.")]
public static class LabelEndpoints
{
    /// <summary>Return the caller's labels (the roster), ordered by name.</summary>
    [WolverineGet("/api/labels")]
    public static Task<IReadOnlyList<LabelResponse>> List(IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<IReadOnlyList<LabelResponse>>(new ListLabels());
    }

    /// <summary>
    /// Idempotent insert-if-not-exists of the caller's label, keyed on the client-generated id in the route.
    /// The owner is resolved from <c>ICurrentUser</c> inside the handler — never the body. Returns 200 on both
    /// first insert and replay.
    /// </summary>
    [WolverinePut("/api/labels/{id}")]
    public static Task<LabelResponse> Create(Guid id, CreateLabelRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<LabelResponse>(new CreateLabel
        {
            Id = LabelId.From(id),
            Name = request.Name,
            Color = request.Color,
        });
    }

    /// <summary>Rename and/or recolor the caller's label (whole-object replace). Not-owned/absent → 404.</summary>
    [WolverinePatch("/api/labels/{id}")]
    public static Task<LabelResponse> Update(Guid id, UpdateLabelRequest request, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<LabelResponse>(new UpdateLabel
        {
            Id = LabelId.From(id),
            Name = request.Name,
            Color = request.Color,
        });
    }

    /// <summary>Hard-delete the caller's label (FK cascade clears its applications). Not-owned/absent → 404. Returns 204.</summary>
    [WolverineDelete("/api/labels/{id}")]
    public static Task Delete(Guid id, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync(new DeleteLabel { Id = LabelId.From(id) });
    }
}
