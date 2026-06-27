using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.TaskManagement;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Edits the caller's own project (AS-07/AS-08/AS-09, contracts/openapi.yaml <c>editProject</c>) as a
/// WHOLE-OBJECT replace (research R4): name/color/icon/parentId together, under the optimistic
/// <c>version</c> guard. Re-parenting is supplying a different <see cref="ParentId"/>; it triggers the
/// 404-before-422 ownership + one-level-nesting precedence (R3). The caller is resolved from
/// <see cref="ICurrentUser"/> — the wire NEVER supplies an owner.
/// </summary>
/// <remarks>
/// This is the HTTP request bound by <c>PATCH /api/projects/{id}</c>: <see cref="Id"/> binds from the
/// route, the rest from the body. <see cref="ParentId"/> is authoritative (null = top-level), required
/// at the boundary (R4) so a name-only edit re-sends the current parent rather than silently un-parenting.
/// </remarks>
public sealed record EditProject
{
    /// <summary>The project identity, carried in the route.</summary>
    public required ProjectId Id { get; init; }

    /// <summary>The new project name; trimmed-non-empty and ≤ 200 chars.</summary>
    public required string Name { get; init; }

    /// <summary>The new preset color token (R10/ASM-04); membership validated at the boundary.</summary>
    public required string Color { get; init; }

    /// <summary>The new preset icon token (R10/ASM-04); membership validated at the boundary.</summary>
    public required string Icon { get; init; }

    /// <summary>The new top-level parent, or null for top-level (R4 whole-object replace).</summary>
    public required ProjectId? ParentId { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token (R4); a stale value → 409.</summary>
    public required int Version { get; init; }
}

/// <summary>
/// Validates <see cref="EditProject"/> at the boundary — the command-LOCAL checks only (mirrors
/// <see cref="CreateProjectValidator"/>): name bounds, color/icon preset membership, and the self-parent
/// guard. The CROSS-ROW nesting rule (R3) lives in the handler. A violation → <c>422 validation_failed</c>.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors slice-002 RenameTaskValidator).")]
public sealed class EditProjectValidator : AbstractValidator<EditProject>
{
    private const int MaxNameLength = 200;

    public EditProjectValidator()
    {
        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Name must not be empty.")
            .Must(name => name is null || name.Trim().Length <= MaxNameLength)
            .WithMessage($"Name must be {MaxNameLength} characters or fewer.");

        RuleFor(x => x.Color)
            .Must(ProjectPresets.IsValidColor)
            .WithMessage("Color must be one of the preset colors.");

        RuleFor(x => x.Icon)
            .Must(ProjectPresets.IsValidIcon)
            .WithMessage("Icon must be one of the preset icons.");

        RuleFor(x => x.ParentId)
            .Must((command, parentId) => parentId is null || parentId != command.Id)
            .WithMessage("A project cannot be its own parent.");
    }
}

/// <summary>
/// Handles <see cref="EditProject"/> under the optimistic-concurrency <c>version</c> rule (R4) with the
/// 404-before-422 parent resolution + one-level-nesting guard (R3/R13). Authentication is enforced
/// upstream by the deny-by-default middleware.
/// </summary>
/// <remarks>
/// Decision path:
/// <list type="bullet">
/// <item>owner-scoped + NON-deleted load; a foreign/absent/soft-deleted id → 404 (NEVER 403, R13).</item>
/// <item>a stale <see cref="EditProject.Version"/> → 409 <c>version_conflict</c>, BEFORE applying the
/// edit so a rejected request leaves the row untouched.</item>
/// <item>resolve the (possibly changed) parent as OWNED (foreign/absent → 404), then the nesting guard
/// with BOTH cross-row facts: the parent's top-level status, and whether THIS project has children
/// (owned-but-illegal → 422, R3). Re-parenting to null (top-level) skips the parent resolution.</item>
/// <item>otherwise <c>Project.Edit</c> (bumps <c>Version</c> + stamps <c>UpdatedAt</c>) and persist; the
/// interleaved-race backstop is closed at <c>ProjectRepository.SaveChangesAsync</c> (→ 409).</item>
/// </list>
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-002 RenameTaskHandler).")]
public static class EditProjectHandler
{
    public static async Task<ProjectResponse> Handle(
        EditProject command,
        ICurrentUser currentUser,
        IProjectRepository projects,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(projects);

        var owner = currentUser.Id;

        var project = await projects
            .FindOwnedAsync(command.Id, owner, cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
        {
            throw new NotFoundException();
        }

        if (project.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        // 404-before-422 parent resolution + nesting guard (R3/R13). On edit BOTH failure shapes can
        // fire: the chosen parent is a child, OR this project has its own children.
        var children = command.ParentId is null
            ? []
            : await projects.ListChildrenAsync(command.Id, owner, cancellationToken).ConfigureAwait(false);
        await CreateProjectHandler
            .EnsureParentAllowedAsync(command.ParentId, owner, projects, projectHasChildren: children.Count > 0, cancellationToken)
            .ConfigureAwait(false);

        project.Edit(command.Name, command.Color, command.Icon, command.ParentId, DateTime.UtcNow);
        await projects.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ProjectResponse.From(project);
    }
}
