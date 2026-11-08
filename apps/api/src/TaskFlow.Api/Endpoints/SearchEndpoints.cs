using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.TaskManagement.Queries;
using TaskFlow.Application.TaskManagement.Search;
using Wolverine;
using Wolverine.Http;

namespace TaskFlow.Api.Endpoints;

/// <summary>
/// The command palette search (slice 013, contracts/search-api.md): one access-scoped read over tasks,
/// projects and labels. Behind the deny-by-default authenticated gate like every handler (FR-068).
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine.Http discovers and maps HTTP endpoints only on public types; this class must stay public.")]
public static class SearchEndpoints
{
    /// <summary>
    /// Fuzzy (token-AND, case-insensitive substring) search across the caller's readable tasks (title +
    /// description), owned ∪ member projects (archived included) and own labels (FR-032). <c>q</c> longer than
    /// 200 characters → 422; a blank <c>q</c> → three empty arrays.
    /// </summary>
    [WolverineGet("/api/search")]
    public static Task<SearchResponse> Search(IMessageBus bus, string q = "")
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<SearchResponse>(new SearchTasksProjectsLabels { Q = q });
    }
}
