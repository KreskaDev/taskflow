using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Errors;
using Cycle = TaskFlow.Domain.TaskManagement.Cycle;
using CycleId = TaskFlow.Domain.TaskManagement.CycleId;

namespace TaskFlow.Application.TaskManagement.Cycles;

/// <summary>
/// Idempotent insert-if-not-exists of a cycle, keyed on the client-generated id (slice 011,
/// contracts/cycles-api.md <c>createCycle</c>). A TEAM-WIDE operation: any authenticated,
/// admitted user may create a cycle (spec IX) — there is no owner column; authentication is
/// enforced upstream by the deny-by-default middleware.
/// </summary>
public sealed record CreateCycle
{
    /// <summary>The cycle identity, carried in the route (client-generated UUIDv7).</summary>
    public required CycleId Id { get; init; }

    /// <summary>The cycle name; trimmed-non-empty and ≤ 200 chars.</summary>
    public required string Name { get; init; }

    /// <summary>Start instant (UTC); must be strictly before <see cref="EndDate"/>.</summary>
    public required DateTime StartDate { get; init; }

    /// <summary>End instant (UTC).</summary>
    public required DateTime EndDate { get; init; }
}

/// <summary>
/// Validates <see cref="CreateCycle"/> at the boundary (contracts/cycles-api.md): name
/// trimmed-non-empty ≤ 200, both dates present (a missing JSON date binds
/// <c>default(DateTime)</c>), and <c>start &lt; end</c> — the ONLY cross-field rule; overlap
/// between cycles is deliberately NOT validated (Clarifications 2026-08-16).
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware (mirrors slice-002 validators).")]
public sealed class CreateCycleValidator : AbstractValidator<CreateCycle>
{
    public CreateCycleValidator()
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
/// Handles <see cref="CreateCycle"/>: an existing row with the id is the idempotent success
/// (returns the persisted cycle unchanged); otherwise insert. The PK unique index is the
/// concurrent double-insert backstop — the race loser re-resolves to the same 200.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen.")]
public static class CreateCycleHandler
{
    public static async Task<CycleResponse> Handle(
        CreateCycle command,
        ICycleRepository cycles,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(cycles);

        var existing = await cycles.FindByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return await ToResponseAsync(existing, cycles, cancellationToken).ConfigureAwait(false);
        }

        var cycle = Cycle.Create(command.Id, command.Name, command.StartDate, command.EndDate, DateTime.UtcNow);
        cycles.Add(cycle);
        try
        {
            await cycles.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateCycleIdException)
        {
            // Concurrent same-id PUT lost the race at the PK — re-resolve to the idempotent 200.
            var winner = await cycles.FindByIdAsync(command.Id, cancellationToken).ConfigureAwait(false)
                ?? throw new NotFoundException();
            return await ToResponseAsync(winner, cycles, cancellationToken).ConfigureAwait(false);
        }

        return CycleResponse.From(cycle, CycleTaskCounts.Empty);
    }

    private static async Task<CycleResponse> ToResponseAsync(Cycle cycle, ICycleRepository cycles, CancellationToken cancellationToken)
    {
        var counts = await cycles.CountTasksForCycleAsync(cycle.Id.Value, cancellationToken).ConfigureAwait(false);
        return CycleResponse.From(cycle, counts);
    }
}
