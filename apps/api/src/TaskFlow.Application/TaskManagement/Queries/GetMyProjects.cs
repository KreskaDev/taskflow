using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;

namespace TaskFlow.Application.TaskManagement.Queries;

/// <summary>
/// Lists the calling user's own non-deleted projects (R8, contracts/openapi.yaml <c>listProjects</c>) —
/// the flat list the sidebar/archived disclosure assemble into a one-level tree client-side (R16). The
/// owner is resolved from <see cref="ICurrentUser"/>, never a wire field, so a caller can only ever see
/// projects it owns (R13: owner-scoped, never an enumeration oracle).
/// </summary>
/// <remarks>
/// Two DISJOINT view sets driven by <see cref="Archived"/> (R8): <c>false</c> (default) → the ACTIVE set
/// (<c>archived_at IS NULL</c>) for the sidebar; <c>true</c> → the ARCHIVED set (<c>archived_at IS NOT
/// NULL</c>) so unarchive (AS-11) is reachable. Both are always <c>deleted_at IS NULL</c>.
/// </remarks>
public sealed record GetMyProjects
{
    /// <summary>Whether to return the ARCHIVED set instead of the default ACTIVE set (R8).</summary>
    public bool Archived { get; init; }
}

/// <summary>
/// Handles <see cref="GetMyProjects"/>. Authentication is enforced upstream by the deny-by-default
/// middleware; this handler owns only the owner-scoped read. The repository query applies
/// <c>WHERE owner_id = owner AND deleted_at IS NULL AND (archived_at IS [NOT] NULL)</c>, so the handler
/// just projects each row to its lean <see cref="ProjectResponse"/> wire model.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-002 GetMyTasksHandler).")]
public static class GetMyProjectsHandler
{
    public static async Task<IReadOnlyList<ProjectResponse>> Handle(
        GetMyProjects query,
        ICurrentUser currentUser,
        IProjectRepository projects,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(projects);

        var owned = await projects
            .ListOwnedAsync(currentUser.Id, query.Archived, cancellationToken)
            .ConfigureAwait(false);

        return owned.Select(ProjectResponse.From).ToList();
    }
}
