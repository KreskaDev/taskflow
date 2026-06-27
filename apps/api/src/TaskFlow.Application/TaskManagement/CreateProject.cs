using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.TaskManagement;
using Task = System.Threading.Tasks.Task;

namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// Idempotent insert-if-not-exists of a project by the client-generated id (FR-001 parity, research
/// R2). The caller is resolved from <see cref="ICurrentUser"/> — the wire NEVER supplies an
/// <c>ownerId</c>, so a caller can only ever create a project it owns (R13).
/// </summary>
/// <remarks>
/// This is the HTTP request bound by <c>PUT /api/projects/{id}</c>: <see cref="Id"/> binds from the
/// route, the rest from the body. <see cref="ParentId"/> is optional on create (null = top-level).
/// The one-level-nesting rule (R3) is enforced in the handler with the repository (cross-row), AFTER
/// the 404-before-422 ownership resolution of the parent.
/// </remarks>
public sealed record CreateProject
{
    /// <summary>The client-generated UUIDv7 identity, carried in the route (FR-001).</summary>
    public required ProjectId Id { get; init; }

    /// <summary>The project name; trimmed-non-empty and ≤ 200 chars.</summary>
    public required string Name { get; init; }

    /// <summary>A preset color token (R10/ASM-04); membership validated at the boundary.</summary>
    public required string Color { get; init; }

    /// <summary>A preset icon token (R10/ASM-04); membership validated at the boundary.</summary>
    public required string Icon { get; init; }

    /// <summary>The top-level parent, or null for a top-level project (one-level rule, R3).</summary>
    public ProjectId? ParentId { get; init; }
}

/// <summary>
/// Validates <see cref="CreateProject"/> at the boundary (research R10/R16) — the command-LOCAL checks
/// only: <see cref="CreateProject.Name"/> trimmed-non-empty and ≤ 200 chars, <see cref="CreateProject.Color"/>
/// / <see cref="CreateProject.Icon"/> members of the frozen preset sets (<see cref="ProjectPresets"/>),
/// and the self-parent guard (a project may not be its own parent). The CROSS-ROW nesting rule (R3) is
/// NOT here — it needs repository lookups and lives in the handler. A violation surfaces as
/// <c>422 validation_failed</c> via the wired Wolverine FluentValidation + <c>ProblemDetailsMiddleware</c>
/// pipeline (no new error code, R12).
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors slice-002 CreateTaskValidator).")]
public sealed class CreateProjectValidator : AbstractValidator<CreateProject>
{
    private const int MaxNameLength = 200;

    public CreateProjectValidator()
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

        // Self-parent is a command-local check (no repository lookup): a project cannot be its own parent.
        RuleFor(x => x.ParentId)
            .Must((command, parentId) => parentId is null || parentId != command.Id)
            .WithMessage("A project cannot be its own parent.");
    }
}

/// <summary>
/// Handles <see cref="CreateProject"/> as insert-if-not-exists keyed on the client id (research R2),
/// then applies the 404-before-422 parent resolution + one-level-nesting guard (R3/R13). Authentication
/// is enforced upstream by the deny-by-default middleware; this handler owns the idempotent-create +
/// ownership-disclosure + nesting logic only.
/// </summary>
/// <remarks>
/// Decision path:
/// <list type="bullet">
/// <item>owner-scoped + tombstone-INCLUSIVE load (so a foreign/own-tombstoned id is distinguishable):
/// an own live row → idempotent replay UNCHANGED; an own tombstone → the id is spent → 404; absent or
/// foreign → attempt the insert.</item>
/// <item>BEFORE inserting a child, resolve the parent as OWNED (foreign/absent → 404, no existence leak,
/// R13), then apply the pure one-level-nesting guard (owned-but-illegal → 422, R3). A freshly created
/// project has no children, so only the parent-is-a-child failure shape can fire on create.</item>
/// <item>the DB primary key is the race backstop; a concurrent double-insert surfaces as
/// <see cref="DuplicateProjectIdException"/>, re-resolved through the SAME find-then-decide path.</item>
/// </list>
/// </remarks>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors slice-002 CreateTaskHandler).")]
public static class CreateProjectHandler
{
    public static async Task<ProjectResponse> Handle(
        CreateProject command,
        ICurrentUser currentUser,
        IProjectRepository projects,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(projects);

        var owner = currentUser.Id;

        var existing = await projects
            .FindOwnedIncludingDeletedAsync(command.Id, owner, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return Resolve(existing);
        }

        // 404-before-422 parent resolution + nesting guard (R3/R13). On create the project has no
        // children yet, so projectHasChildren is always false here.
        await EnsureParentAllowedAsync(command.ParentId, owner, projects, projectHasChildren: false, cancellationToken)
            .ConfigureAwait(false);

        var created = Project.Create(
            command.Id, owner, command.Name, command.Color, command.Icon, command.ParentId, DateTime.UtcNow);
        projects.Add(created);
        try
        {
            await projects.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ProjectResponse.From(created);
        }
        catch (DuplicateProjectIdException)
        {
            // A concurrent insert (or a foreign id) holds the PK. Re-resolve through the SAME
            // find-then-decide path: own live row → idempotent hit; own tombstone or still-foreign → 404.
            var resolved = await projects
                .FindOwnedIncludingDeletedAsync(command.Id, owner, cancellationToken)
                .ConfigureAwait(false);
            if (resolved is null)
            {
                throw new NotFoundException();
            }

            return Resolve(resolved);
        }
    }

    /// <summary>
    /// Resolves the candidate parent as caller-owned (foreign/absent → 404, R13) and then applies the
    /// pure one-level-nesting guard (owned-but-illegal → 422 via <see cref="ValidationException"/>, R3).
    /// Shared precedence with edit/move. A null <paramref name="parentId"/> (top-level) is a no-op.
    /// </summary>
    internal static async Task EnsureParentAllowedAsync(
        ProjectId? parentId, UserId owner, IProjectRepository projects, bool projectHasChildren = false,
        CancellationToken cancellationToken = default)
    {
        if (parentId is null)
        {
            return;
        }

        var parent = await projects
            .FindOwnedAsync(parentId.Value, owner, cancellationToken)
            .ConfigureAwait(false);
        if (parent is null)
        {
            // Foreign / absent / tombstoned parent → 404 (existence not disclosed), BEFORE the nesting 422.
            throw new NotFoundException();
        }

        try
        {
            Project.EnsureNestingAllowed(parentId, parent.ParentId is null, projectHasChildren);
        }
        catch (InvalidOperationException ex)
        {
            // Owned-but-illegal nesting → 422 validation_failed on parentId (R3/R12), via the field-level
            // ValidationException the Wolverine pipeline maps through ProblemDetailsMiddleware.
            throw new ValidationException(
                [new FluentValidation.Results.ValidationFailure(nameof(CreateProject.ParentId), ex.Message)]);
        }
    }

    private static ProjectResponse Resolve(Project existing)
    {
        // The id is the caller's own already-soft-deleted row: the id is spent, treat as not-found.
        if (existing.DeletedAt is not null)
        {
            throw new NotFoundException();
        }

        // Idempotent replay: return the existing row UNCHANGED (no overwrite, no version bump).
        return ProjectResponse.From(existing);
    }
}
