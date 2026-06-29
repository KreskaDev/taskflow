using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using Label = TaskFlow.Domain.TaskManagement.Label;
using LabelId = TaskFlow.Domain.TaskManagement.LabelId;

namespace TaskFlow.Application.TaskManagement.Labels;

/// <summary>
/// The HTTP request body for <c>PATCH /api/labels/{id}</c> (contracts/openapi.yaml <c>updateLabel</c>) —
/// a whole-object replace realizing both rename and recolor.
/// </summary>
public sealed record UpdateLabelRequest
{
    /// <summary>The new label name; trimmed-non-empty and ≤ 50 chars.</summary>
    public required string Name { get; init; }

    /// <summary>The new optional preset color token, or null to clear.</summary>
    public string? Color { get; init; }
}

/// <summary>
/// Renames and/or recolors the caller's label (R3) — a whole-object replace. Ownership-gated (not-owned/absent
/// → 404, uniform); a duplicate <c>(owner, normalized name)</c> → 422.
/// </summary>
public sealed record UpdateLabel
{
    /// <summary>The label identity, carried in the route.</summary>
    public required LabelId Id { get; init; }

    /// <summary>The new label name.</summary>
    public required string Name { get; init; }

    /// <summary>The new optional preset color token.</summary>
    public string? Color { get; init; }
}

/// <summary>
/// Validates <see cref="UpdateLabel"/> at the boundary (R7) — same rules as create. A violation → 422.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors CreateLabelValidator).")]
public sealed class UpdateLabelValidator : AbstractValidator<UpdateLabel>
{
    private const int MaxNameLength = 50;

    public UpdateLabelValidator()
    {
        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Name is required.")
            .Must(name => name is null || name.Trim().Length <= MaxNameLength)
            .WithMessage($"Name must be {MaxNameLength} characters or fewer.");

        RuleFor(x => x.Color)
            .Must(color => color is null || ProjectPresets.IsValidColor(color))
            .WithMessage("Color must be one of the preset colors.");
    }
}

/// <summary>
/// Handles <see cref="UpdateLabel"/> (R3/R4). Owner-scoped (Tier A): load the caller's label (not-owned/absent
/// → 404, uniform existence-hide), a normalized-name pre-check EXCLUDING self (→ 422), then the whole-object Edit.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors EditTaskHandler).")]
public static class UpdateLabelHandler
{
    public static async Task<LabelResponse> Handle(
        UpdateLabel command,
        ICurrentUser currentUser,
        ILabelRepository labels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(labels);

        var owner = currentUser.Id;

        var label = await labels.FindOwnedAsync(command.Id, owner, cancellationToken).ConfigureAwait(false);
        if (label is null)
        {
            throw new NotFoundException();
        }

        var normalized = Label.NormalizeForUniqueness(command.Name);
        if (await labels.ExistsByNormalizedNameForOwnerAsync(owner, normalized, excludingId: command.Id, cancellationToken).ConfigureAwait(false))
        {
            throw new ValidationException("A label with this name already exists.");
        }

        label.Edit(command.Name, command.Color, DateTime.UtcNow);
        await labels.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return LabelResponse.From(label);
    }
}
