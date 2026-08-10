using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.TaskManagement.Queries;
using Wolverine;
using Wolverine.Http;

namespace TaskFlow.Api.Endpoints;

/// <summary>
/// HTTP surface for the sidebar view counts (slice 019, FR-109; contracts/view-counts.md). A thin
/// transport adapter dispatching through Wolverine's local pipeline so the deny-by-default
/// authorization middleware is woven ahead of the handler (FR-068).
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine.Http discovers and maps HTTP endpoints only on public types; this class must stay public.")]
public static class ViewCountsEndpoints
{
    /// <summary>
    /// The caller's per-view incomplete-task counts (Inbox/Today/Upcoming/Assigned + one entry per
    /// accessible non-archived project). Authorization-scoped to the caller; an authenticated caller
    /// with zero accessible data receives zeros and an empty array, not an error.
    /// </summary>
    [WolverineGet("/api/views/counts")]
    public static Task<ViewCountsResponse> Counts(IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<ViewCountsResponse>(new GetViewCounts());
    }
}
