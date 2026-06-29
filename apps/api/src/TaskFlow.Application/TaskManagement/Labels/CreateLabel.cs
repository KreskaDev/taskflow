using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using Label = TaskFlow.Domain.TaskManagement.Label;
using LabelId = TaskFlow.Domain.TaskManagement.LabelId;

namespace TaskFlow.Application.TaskManagement.Labels;

/// <summary>
/// The HTTP request body for <c>PUT /api/labels/{id}</c> (contracts/openapi.yaml <c>createLabel</c>). The id
/// is the client-generated route parameter; the owner is resolved from <see cref="ICurrentUser"/>, never the wire.
/// </summary>
public sealed record CreateLabelRequest
{
    /// <summary>The label name; trimmed-non-empty and ≤ 50 chars.</summary>
    public required string Name { get; init; }

    /// <summary>An optional preset color token, or null.</summary>
    public string? Color { get; init; }
}

/// <summary>
/// Creates (idempotent-upsert) a per-user label keyed on the client-generated id (R3/R4). Re-`PUT`ting the
/// same id returns the existing label UNCHANGED (idempotent replay, mirrors <c>CreateTask</c>); a duplicate
/// <c>(owner, normalized name)</c> → 422.
/// </summary>
public sealed record CreateLabel
{
    /// <summary>The label identity, carried in the route (client-generated UUIDv7).</summary>
    public required LabelId Id { get; init; }

    /// <summary>The label name.</summary>
    public required string Name { get; init; }

    /// <summary>An optional preset color token.</summary>
    public string? Color { get; init; }
}

/// <summary>
/// Validates <see cref="CreateLabel"/> at the boundary (R7): a trimmed-non-empty name ≤ 50 chars and a
/// null-or-preset color. A violation → 422 <c>validation_failed</c>. Per-owner name uniqueness is a cross-row
/// rule enforced in the handler + the DB unique index, not here.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors CreateProjectValidator).")]
public sealed class CreateLabelValidator : AbstractValidator<CreateLabel>
{
    private const int MaxNameLength = 50;

    public CreateLabelValidator()
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
/// Handles <see cref="CreateLabel"/> (R3/R4). Authentication is enforced upstream by the deny-by-default
/// middleware. Owner-scoped (Tier A): the id-keyed idempotent replay returns the existing row; otherwise a
/// normalized-name pre-check (→ 422) precedes the insert (the DB unique index is the race backstop).
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen (mirrors CreateTaskHandler).")]
public static class CreateLabelHandler
{
    public static async Task<LabelResponse> Handle(
        CreateLabel command,
        ICurrentUser currentUser,
        ILabelRepository labels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(labels);

        var owner = currentUser.Id;

        // Idempotent replay: re-PUT of the caller's own id returns the existing label UNCHANGED (no overwrite).
        var existing = await labels.FindOwnedAsync(command.Id, owner, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return LabelResponse.From(existing);
        }

        // Per-owner case-insensitive uniqueness pre-check (the friendly 422 ahead of the DB index backstop).
        var normalized = Label.NormalizeForUniqueness(command.Name);
        if (await labels.ExistsByNormalizedNameForOwnerAsync(owner, normalized, excludingId: null, cancellationToken).ConfigureAwait(false))
        {
            throw new ValidationException("A label with this name already exists.");
        }

        var label = Label.Create(command.Id, owner, command.Name, command.Color, DateTime.UtcNow);
        labels.Add(label);
        await labels.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return LabelResponse.From(label);
    }
}
