using System.Diagnostics.CodeAnalysis;
using TaskFlow.Domain.TaskManagement;

namespace TaskFlow.Application.TaskManagement.Recurrence;

/// <summary>
/// The wire shape of ENT-05 (slice 012, contracts/recurrence-api.md): a flat record whose members depend on
/// <see cref="Type"/> — <c>daily</c> (no members), <c>every_n_days</c> (+<see cref="Interval"/>),
/// <c>weekdays</c> (+<see cref="Weekdays"/>, ISO 1..7), <c>monthly</c> (+<see cref="DayOfMonth"/>). Bound as part
/// of <c>EditTaskRequest</c> and emitted on every task response; <see cref="IsValid"/> backs the
/// FluentValidation rule so a bad shape is a friendly <c>422 validation_failed</c> before the Domain sees it.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Bound by Wolverine.Http inside request bodies and emitted into the generated OpenAPI/client schema; must stay public.")]
public sealed record RecurrenceRuleDto
{
    public required string Type { get; init; }

    public int? Interval { get; init; }

    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Wire DTO — arrays serialize as JSON lists and bind symmetrically.")]
    public int[]? Weekdays { get; init; }

    public int? DayOfMonth { get; init; }

    /// <summary>True iff <see cref="ToDomain"/> would succeed (the closed set + ranges of research D1).</summary>
    public static bool IsValid(RecurrenceRuleDto? dto)
    {
        if (dto is null)
        {
            return true;
        }

        try
        {
            _ = dto.ToDomain();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public RecurrenceRule ToDomain() => RecurrenceRule.Create(Type, Interval, Weekdays, DayOfMonth);

    public static RecurrenceRuleDto? FromDomain(RecurrenceRule? rule) => rule switch
    {
        null => null,
        DailyRecurrence => new RecurrenceRuleDto { Type = rule.Type },
        EveryNDaysRecurrence n => new RecurrenceRuleDto { Type = rule.Type, Interval = n.Interval },
        WeekdaysRecurrence w => new RecurrenceRuleDto { Type = rule.Type, Weekdays = w.Days.ToArray() },
        MonthlyRecurrence m => new RecurrenceRuleDto { Type = rule.Type, DayOfMonth = m.DayOfMonth },
        _ => throw new ArgumentOutOfRangeException(nameof(rule), rule.Type, "Unknown recurrence rule type."),
    };
}
