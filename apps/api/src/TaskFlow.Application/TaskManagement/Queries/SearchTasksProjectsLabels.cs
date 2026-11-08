using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.TaskManagement.Labels;
using TaskFlow.Application.TaskManagement.Search;
using TaskFlow.Domain.TaskManagement;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;
using TaskStatus = TaskFlow.Domain.TaskManagement.TaskStatus;

namespace TaskFlow.Application.TaskManagement.Queries;

/// <summary>
/// The command palette search (slice 013, US-04, FR-032/FR-034 — contracts/search-api.md): bound by
/// <c>GET /api/search?q=</c>. <see cref="Q"/> is the raw query (≤ 200 chars at the boundary).
/// </summary>
public sealed record SearchTasksProjectsLabels
{
    /// <summary>The raw query text.</summary>
    public required string Q { get; init; }
}

/// <summary>Boundary validation: the query is at most 200 characters (longer → <c>422 validation_failed</c>).</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors slice-002 CreateTaskValidator posture).")]
public sealed class SearchTasksProjectsLabelsValidator : AbstractValidator<SearchTasksProjectsLabels>
{
    public const int MaxQueryLength = 200;

    public SearchTasksProjectsLabelsValidator()
    {
        RuleFor(x => x.Q)
            .Must(q => q is null || q.Length <= MaxQueryLength)
            .WithMessage($"Query must be {MaxQueryLength} characters or fewer.");
    }
}

/// <summary>
/// Handles <see cref="SearchTasksProjectsLabels"/> (research D1–D4). Authentication is enforced upstream by the
/// deny-by-default middleware; THIS handler owns the dispatch-by-visibility scoping (FR-065–FR-068): the caller's
/// current membership project ids are resolved ONCE, then tasks are matched under the readable predicate
/// (<c>created_by = caller OR project_id ∈ memberships</c>), projects as owned ∪ member projects (both archive
/// states — US-10.AS-06), labels as the caller's own. A former member's project ids are simply absent, so nothing
/// of that project can match; <c>createdBy</c>/assignee never widen the set. Candidates come back capped from the
/// repositories and are ranked in memory (<see cref="SearchTokens.Rank"/>) before the bucket caps apply.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-002 GetMyTasksHandler).")]
public static class SearchTasksProjectsLabelsHandler
{
    /// <summary>Bucket caps (contracts/search-api.md).</summary>
    public const int TaskCap = 20;

    public const int ProjectCap = 10;

    public const int LabelCap = 10;

    /// <summary>How many matching rows a repository returns before in-memory ranking (short queries match many rows).</summary>
    private const int CandidateFetch = 200;

    public static async Task<SearchResponse> Handle(
        SearchTasksProjectsLabels query,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        ILabelRepository labels,
        ITaskLabelRepository taskLabels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(taskLabels);

        var tokens = SearchTokens.Tokenize(query.Q);
        if (tokens.Count == 0)
        {
            return SearchResponse.Empty;
        }

        var caller = currentUser.Id;
        var normalized = string.Join(' ', tokens);

        // Resolved ONCE per request and shared by the task and project buckets (D4): the caller's readable
        // projects = owned (personal + shared-owned, both archive states) ∪ CURRENT memberships.
        var memberProjectIds = await members.ListProjectIdsForUserAsync(caller, cancellationToken).ConfigureAwait(false);
        var ownedProjectIds = await projects.ListOwnedProjectIdsAsync(caller, cancellationToken).ConfigureAwait(false);
        var readableProjectIds = ownedProjectIds.Concat(memberProjectIds).Distinct().ToList();

        var taskRows = await tasks
            .SearchReadableAsync(caller, readableProjectIds, tokens, CandidateFetch, cancellationToken)
            .ConfigureAwait(false);
        var rankedTasks = taskRows
            .OrderBy(t => SearchTokens.Rank(t.Title, normalized))
            .ThenBy(t => t.Status == TaskStatus.Done || t.Status == TaskStatus.Cancelled ? 1 : 0)
            .ThenByDescending(t => t.UpdatedAt)
            .ThenBy(t => t.Id.Value)
            .Take(TaskCap)
            .ToList();
        var labelsByTask = await taskLabels
            .ListLabelIdsForTasksAsync(rankedTasks.Select(t => t.Id).ToList(), caller, cancellationToken)
            .ConfigureAwait(false);
        var taskResponses = rankedTasks
            .Select(t => TaskResponse.From(t, labelsByTask.TryGetValue(t.Id, out var ids) ? ids : []))
            .ToList();

        var projectRows = await projects
            .SearchReadableAsync(caller, memberProjectIds, tokens, CandidateFetch, cancellationToken)
            .ConfigureAwait(false);
        var rankedProjects = projectRows
            .OrderBy(p => SearchTokens.Rank(p.Name, normalized))
            .ThenBy(p => p.ArchivedAt is null ? 0 : 1)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ThenBy(p => p.Id.Value)
            .Take(ProjectCap)
            .ToList();
        var projectResponses = new List<ProjectResponse>(rankedProjects.Count);
        foreach (var project in rankedProjects)
        {
            var role = EffectiveRole.Owner;
            if (project.OwnerId != caller)
            {
                var row = await members.FindAsync(project.Id, caller, cancellationToken).ConfigureAwait(false);
                role = row?.Role == MembershipRoles.Editor ? EffectiveRole.Editor : EffectiveRole.Viewer;
            }

            projectResponses.Add(ProjectResponse.From(project, role));
        }

        var labelRows = await labels
            .SearchForOwnerAsync(caller, tokens, CandidateFetch, cancellationToken)
            .ConfigureAwait(false);
        var labelResponses = labelRows
            .OrderBy(l => SearchTokens.Rank(l.Name, normalized))
            .ThenBy(l => l.Name, StringComparer.Ordinal)
            .Take(LabelCap)
            .Select(LabelResponse.From)
            .ToList();

        return new SearchResponse { Tasks = taskResponses, Projects = projectResponses, Labels = labelResponses };
    }
}
