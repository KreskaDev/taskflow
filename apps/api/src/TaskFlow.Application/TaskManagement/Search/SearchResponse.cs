using TaskFlow.Application.TaskManagement.Labels;

namespace TaskFlow.Application.TaskManagement.Search;

/// <summary>
/// The command palette's read model (slice 013, contracts/search-api.md D1): three access-scoped, ranked,
/// capped buckets built from the EXISTING row shapes — no new entity. Actions are a client-side catalogue
/// and never cross the API.
/// </summary>
public sealed record SearchResponse
{
    /// <summary>Caller-readable tasks whose title or description matches every token (≤ 20, ranked).</summary>
    public required IReadOnlyList<TaskResponse> Tasks { get; init; }

    /// <summary>Owned ∪ member projects whose name matches (≤ 10, archived included and sorted last).</summary>
    public required IReadOnlyList<ProjectResponse> Projects { get; init; }

    /// <summary>The caller's own labels whose name matches (≤ 10).</summary>
    public required IReadOnlyList<LabelResponse> Labels { get; init; }

    /// <summary>The empty result for a blank query.</summary>
    public static SearchResponse Empty { get; } = new() { Tasks = [], Projects = [], Labels = [] };
}
