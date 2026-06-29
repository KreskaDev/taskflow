using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using LabelId = TaskFlow.Domain.TaskManagement.LabelId;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.Application.TaskManagement.Labels;

/// <summary>
/// The HTTP request body for <c>PATCH /api/tasks/{id}/labels</c> (contracts/openapi.yaml <c>setTaskLabels</c>).
/// The task id is the route parameter; the caller is resolved from <see cref="ICurrentUser"/>. VERSIONLESS
/// (R2) — a per-user label toggle never touches the shared <c>Task.version</c>.
/// </summary>
public sealed record SetTaskLabelsRequest
{
    /// <summary>The desired full set of the CALLER's labels on the task (each MUST be owned by the caller).</summary>
    public required IReadOnlyList<Guid> LabelIds { get; init; }
}

/// <summary>
/// Replaces the CALLER's labels on a task — a per-user whole-set replace (US-08.AS-04, R2/R4). Two-sided
/// authorization: task write-access (dispatch-by-visibility) AND every label owned by the caller. Versionless.
/// </summary>
public sealed record SetTaskLabels
{
    /// <summary>The task identity, carried in the route.</summary>
    public required TaskId Id { get; init; }

    /// <summary>The desired full set of the caller's labels on the task.</summary>
    public required IReadOnlyList<Guid> LabelIds { get; init; }
}

/// <summary>
/// Validates <see cref="SetTaskLabels"/> at the boundary (R4): the label set has no duplicates and a sane
/// cardinality cap. The cross-row caller-owns-every-label check lives in the handler (it needs the caller's
/// label set). A violation → 422 <c>validation_failed</c>.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors SetTaskAssigneesValidator).")]
public sealed class SetTaskLabelsValidator : AbstractValidator<SetTaskLabels>
{
    private const int MaxLabels = 50; // a sane cap per task (ASM-10).

    public SetTaskLabelsValidator()
    {
        RuleFor(x => x.LabelIds)
            .NotNull()
            .Must(ids => ids is null || ids.Count <= MaxLabels)
            .WithMessage($"A task may have at most {MaxLabels} labels.")
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count)
            .WithMessage("Label ids must not contain duplicates.");
    }
}

/// <summary>
/// Handles <see cref="SetTaskLabels"/> (R2/R4). Authentication is enforced upstream by the deny-by-default
/// middleware. Two-sided:
/// <list type="bullet">
/// <item>TASK side — <see cref="TaskAccessGuards.LoadWritableTaskAsync"/> with <see cref="EffectiveRole.Editor"/>
/// (personal-foreign → 404, shared non-member → 404, shared viewer → 403).</item>
/// <item>LABEL side — every id in <c>LabelIds</c> MUST be owned by the caller, else 422 (uniform, no existence
/// leak); no row is changed.</item>
/// </list>
/// Then the per-user whole-set replace (caller-owned rows only). VERSIONLESS — <c>Task.version</c> is untouched;
/// the response's <c>version</c> is the task's current value, and <c>labels</c> is the just-committed set.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors SetTaskAssigneesHandler).")]
public static class SetTaskLabelsHandler
{
    public static async Task<TaskResponse> Handle(
        SetTaskLabels command,
        ICurrentUser currentUser,
        ITaskRepository tasks,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        IResourceAuthorizationPolicy authorization,
        ILabelRepository labels,
        ITaskLabelRepository taskLabels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(taskLabels);

        var owner = currentUser.Id;

        // TASK side: dispatch-by-visibility write gate (foreign/personal → 404, viewer → 403, non-member → 404).
        var task = await TaskAccessGuards
            .LoadWritableTaskAsync(command.Id, EffectiveRole.Editor, currentUser, tasks, projects, members, authorization, cancellationToken)
            .ConfigureAwait(false);

        // LABEL side (Tier A): every id MUST be a label owned by the caller, else 422 — uniformly (no existence
        // leak between "not yours" and "doesn't exist"); nothing is changed.
        var ownedIds = await labels.ListIdsForOwnerAsync(owner, cancellationToken).ConfigureAwait(false);
        var desired = command.LabelIds.Select(LabelId.From).ToList();
        if (desired.Any(id => !ownedIds.Contains(id)))
        {
            throw new ValidationException("Every label must be one you own.");
        }

        // Per-user whole-set replace (caller-owned rows only; other members' labels on a shared task untouched).
        await taskLabels.SetForOwnerAsync(command.Id, owner, desired, cancellationToken).ConfigureAwait(false);

        // VERSIONLESS: no Task mutation, no SaveChanges on the task aggregate. The caller's labels are exactly
        // the just-committed set (validated owned + set-replaced), so reuse it without a re-query.
        return TaskResponse.From(task, command.LabelIds);
    }
}
