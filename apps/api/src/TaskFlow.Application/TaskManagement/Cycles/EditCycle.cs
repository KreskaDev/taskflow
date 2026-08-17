using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Errors;

namespace TaskFlow.Application.TaskManagement.Cycles;

/// <summary>
/// Whole-object replace of a cycle's name + dates under the optimistic-concurrency guard (slice
/// 011, contracts/cycles-api.md <c>editCycle</c>). Legal in EVERY status (FR-020). Team-wide
/// authorized (spec IX); authentication is enforced upstream.
/// </summary>
public sealed record EditCycle
{
    /// <summary>The cycle identity, carried in the route.</summary>
    public required Domain.TaskManagement.CycleId Id { get; init; }

    /// <summary>The new name; trimmed-non-empty and ≤ 200 chars.</summary>
    public required string Name { get; init; }

    /// <summary>The new start instant (UTC); must stay strictly before <see cref="EndDate"/>.</summary>
    public required DateTime StartDate { get; init; }

    /// <summary>The new end instant (UTC).</summary>
    public required DateTime EndDate { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token.</summary>
    public required int Version { get; init; }
}

/// <summary>Validates <see cref="EditCycle"/> — the same boundary rules as create (re-validated on every edit).</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware.")]
public sealed class EditCycleValidator : AbstractValidator<EditCycle>
{
    public EditCycleValidator()
    {
        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 200)
            .WithMessage("Name must be non-empty and at most 200 characters.");
        RuleFor(x => x.StartDate)
            .Must(d => d != default)
            .WithMessage("The start date is required.");
        RuleFor(x => x.EndDate)
            .Must(d => d != default)
            .WithMessage("The end date is required.");
        RuleFor(x => x)
            .Must(x => x.StartDate == default || x.EndDate == default || x.StartDate < x.EndDate)
            .WithMessage("The start date must be strictly before the end date.")
            .OverridePropertyName("startDate");
    }
}

/// <summary>
/// Handles <see cref="EditCycle"/>: load (404) → version-compare (409) → apply ONE combined
/// mutation (a single version bump — the EditTask convention) → persist (the repository closes
/// the interleaved race → 409).
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen.")]
public static class EditCycleHandler
{
    public static async Task<CycleResponse> Handle(
        EditCycle command,
        ICycleRepository cycles,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(cycles);

        var cycle = await cycles.FindByIdAsync(command.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException();

        if (cycle.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        cycle.Edit(command.Name, command.StartDate, command.EndDate, DateTime.UtcNow);
        await cycles.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var counts = await cycles.CountTasksForCycleAsync(cycle.Id.Value, cancellationToken).ConfigureAwait(false);
        return CycleResponse.From(cycle, counts);
    }
}
