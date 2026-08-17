using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;
using CycleStatus = TaskFlow.Domain.TaskManagement.CycleStatus;
using TaskStatus = TaskFlow.Domain.TaskManagement.TaskStatus;

namespace TaskFlow.Application.TaskManagement.Cycles;

/// <summary>
/// The close review commit (slice 011, contracts/cycles-api.md <c>closeCycle</c>, D4/D5/D10):
/// the manual active → closed transition PLUS the rollover of every incomplete task, in ONE
/// transaction. Team-wide authorized (spec IX); the bulk default applies to ALL incomplete tasks
/// (visible or not — a team-wide operation), while <see cref="Overrides"/> may target only
/// caller-VISIBLE tasks (404 posture otherwise; the whole command is rejected, nothing partial).
/// </summary>
public sealed record CloseCycle
{
    /// <summary>The cycle identity, carried in the route.</summary>
    public required Domain.TaskManagement.CycleId Id { get; init; }

    /// <summary>The bulk rollover choice; null defaults to <c>keep</c> (the pure-close path).</summary>
    public string? Rollover { get; init; }

    /// <summary>Optional per-task overrides ("obsłuż pojedynczo", US-05.AS-04).</summary>
    public IReadOnlyList<CloseCycleOverride>? Overrides { get; init; }

    /// <summary>The caller's last-seen optimistic-concurrency token.</summary>
    public required int Version { get; init; }
}

/// <summary>The known rollover choice tokens (FR-018).</summary>
public static class CycleRolloverChoices
{
    public const string Next = "next";
    public const string Backlog = "backlog";
    public const string Keep = "keep";

    /// <summary>Whether <paramref name="choice"/> is a member of the closed set (null = the keep default).</summary>
    public static bool IsValid(string? choice) => choice is null or Next or Backlog or Keep;
}

/// <summary>Validates <see cref="CloseCycle"/>: the rollover and every override choice are closed-set tokens.</summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Discovered + activated by Wolverine's FluentValidation middleware.")]
public sealed class CloseCycleValidator : AbstractValidator<CloseCycle>
{
    public CloseCycleValidator()
    {
        RuleFor(x => x.Rollover)
            .Must(CycleRolloverChoices.IsValid)
            .WithMessage("Rollover must be one of: next, backlog, keep (or omitted).");
        RuleForEach(x => x.Overrides)
            .Must(o => o is not null && o.Choice is CycleRolloverChoices.Next or CycleRolloverChoices.Backlog or CycleRolloverChoices.Keep)
            .WithMessage("Each override choice must be one of: next, backlog, keep.");
    }
}

/// <summary>
/// Handles <see cref="CloseCycle"/> (D4/D5/D10): load (404) → version-compare (409) → active-only
/// guard (422 <c>cycle_not_active</c>) → validate overrides against the caller-VISIBLE incomplete
/// set (404 posture) → resolve the next planned cycle when any effective choice is <c>next</c>
/// (422 <c>no_next_cycle</c> when none) → apply the rollover matrix + the status flip and persist
/// them in ONE SaveChanges (the Wolverine-scoped DbContext commits atomically — a failing step
/// rolls everything back). Completed (done/cancelled) tasks are untouched.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine-discovered handler; public concrete types are required for codegen.")]
public static class CloseCycleHandler
{
    public static async Task<CloseCycleResponse> Handle(
        CloseCycle command,
        ICurrentUser currentUser,
        ICycleRepository cycles,
        IProjectRepository projects,
        IProjectMembershipRepository members,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(cycles);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(members);

        var cycle = await cycles.FindByIdAsync(command.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException();

        if (cycle.Version != command.Version)
        {
            throw new VersionConflictException();
        }

        if (cycle.Status != CycleStatus.Active)
        {
            throw new CycleNotActiveException();
        }

        var tasks = await cycles.ListTasksInCycleAsync(cycle.Id.Value, cancellationToken).ConfigureAwait(false);
        var incomplete = tasks
            .Where(t => t.Status is not TaskStatus.Done and not TaskStatus.Cancelled)
            .ToList();

        // Overrides may target only caller-visible incomplete tasks of THIS cycle (D10): a
        // foreign personal task or an unshared project's task is indistinguishable from absent →
        // 404 for the whole command (nothing partial — the transaction never starts applying).
        var overrides = new Dictionary<Guid, string>();
        if (command.Overrides is { Count: > 0 })
        {
            var readableProjects = await CycleTaskVisibility
                .ListReadableProjectIdsAsync(currentUser, projects, members, cancellationToken)
                .ConfigureAwait(false);
            var visibleIncomplete = incomplete
                .Where(t => CycleTaskVisibility.IsVisible(t, currentUser.Id, readableProjects))
                .Select(t => t.Id.Value)
                .ToHashSet();

            foreach (var entry in command.Overrides)
            {
                if (!visibleIncomplete.Contains(entry.TaskId))
                {
                    throw new NotFoundException();
                }

                overrides[entry.TaskId] = entry.Choice;
            }
        }

        var bulk = command.Rollover ?? CycleRolloverChoices.Keep;
        var needsNext = incomplete.Count > 0
            && (overrides.ContainsValue(CycleRolloverChoices.Next)
                || (bulk == CycleRolloverChoices.Next && incomplete.Any(t => !overrides.ContainsKey(t.Id.Value))
                    // an all-overridden close still validates the bulk token lazily — next is
                    // only resolved when some task actually takes the "next" choice
                    ));
        var next = needsNext
            ? await cycles.FindNextPlannedAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new NoNextCycleException()
            : null;

        var utcNow = DateTime.UtcNow;
        var (rolledToNext, rolledToBacklog, kept) = (0, 0, 0);
        foreach (var task in incomplete)
        {
            var choice = overrides.TryGetValue(task.Id.Value, out var overridden) ? overridden : bulk;
            switch (choice)
            {
                case CycleRolloverChoices.Next:
                    task.RollToCycle(next!.Id.Value, utcNow);
                    rolledToNext++;
                    break;
                case CycleRolloverChoices.Backlog:
                    task.RollToBacklog(utcNow);
                    rolledToBacklog++;
                    break;
                default:
                    task.MarkCarriedOver(utcNow);
                    kept++;
                    break;
            }
        }

        cycle.Close(utcNow);
        await cycles.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var counts = await cycles.CountTasksForCycleAsync(cycle.Id.Value, cancellationToken).ConfigureAwait(false);
        return new CloseCycleResponse
        {
            Cycle = CycleResponse.From(cycle, counts),
            RolledToNext = rolledToNext,
            RolledToBacklog = rolledToBacklog,
            Kept = kept,
        };
    }
}
