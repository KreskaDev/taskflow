using FluentAssertions;
using TaskFlow.Domain.TaskManagement;

namespace TaskFlow.UnitTests.Domain.TaskManagement;

/// <summary>
/// ENT-05 value-object facts (slice 012, T002): the closed JSON union parses/validates/round-trips, and
/// <c>NextDueAfter</c> reproduces EVERY row of the shared fact table in
/// <c>specs/012-recurring-tasks/contracts/recurrence-api.md</c> — the same rows the web mirror
/// (<c>lib/recurrence.ts</c>) asserts, so client preview and server truth agree (FR-092, ASM-09).
/// </summary>
public sealed class RecurrenceRuleTests
{
    // --- parse / validate / round-trip -------------------------------------------------------------

    [Theory]
    [InlineData("""{"type":"daily"}""")]
    [InlineData("""{"type":"every_n_days","interval":3}""")]
    [InlineData("""{"type":"weekdays","weekdays":[1,3]}""")]
    [InlineData("""{"type":"monthly","dayOfMonth":31}""")]
    public void Parse_then_ToJson_round_trips_every_type(string json)
    {
        var rule = RecurrenceRule.Parse(json);

        RecurrenceRule.Parse(rule.ToJson()).Should().Be(rule);
        rule.ToJson().Should().Be(json, "the camelCase storage shape is canonical");
    }

    [Theory]
    [InlineData("""{"type":"yearly"}""", "unknown type")]
    [InlineData("""{"interval":3}""", "missing type")]
    [InlineData("""{"type":"every_n_days","interval":1}""", "interval below 2")]
    [InlineData("""{"type":"every_n_days","interval":366}""", "interval above 365")]
    [InlineData("""{"type":"every_n_days"}""", "interval missing")]
    [InlineData("""{"type":"weekdays","weekdays":[]}""", "empty weekdays")]
    [InlineData("""{"type":"weekdays","weekdays":[1,1]}""", "duplicate weekdays")]
    [InlineData("""{"type":"weekdays","weekdays":[0]}""", "weekday below 1")]
    [InlineData("""{"type":"weekdays","weekdays":[8]}""", "weekday above 7")]
    [InlineData("""{"type":"monthly","dayOfMonth":0}""", "dayOfMonth below 1")]
    [InlineData("""{"type":"monthly","dayOfMonth":32}""", "dayOfMonth above 31")]
    [InlineData("""{"type":"daily","interval":2}""", "member not belonging to the type")]
    [InlineData("""not json""", "malformed")]
    public void Parse_rejects_invalid_documents(string json, string because)
    {
        var act = () => RecurrenceRule.Parse(json);

        act.Should().Throw<ArgumentException>(because);
    }

    [Fact]
    public void Weekdays_are_normalized_ascending_and_compare_by_value()
    {
        var a = new WeekdaysRecurrence([3, 1]);
        var b = new WeekdaysRecurrence([1, 3]);

        a.Days.Should().Equal(1, 3);
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Create_builds_each_type_from_wire_parts()
    {
        RecurrenceRule.Create("daily", null, null, null).Should().Be(new DailyRecurrence());
        RecurrenceRule.Create("every_n_days", 5, null, null).Should().Be(new EveryNDaysRecurrence(5));
        RecurrenceRule.Create("weekdays", null, [5], null).Should().Be(new WeekdaysRecurrence([5]));
        RecurrenceRule.Create("monthly", null, null, 15).Should().Be(new MonthlyRecurrence(15));
    }

    // --- NextDueAfter: the shared fact table (contracts/recurrence-api.md) ---------------------------

    /// <summary>A date-only due stored by the Warsaw-midnight convention (UTC instant of 00:00 Europe/Warsaw).</summary>
    private static DateTime WarsawMidnight(int y, int m, int d) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Unspecified), Warsaw);

    private static DateTime WarsawWallClock(int y, int m, int d, int hh, int mm) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(y, m, d, hh, mm, 0, DateTimeKind.Unspecified), Warsaw);

    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    public static TheoryData<string, int[], int[]> DateOnlyFacts => new()
    {
        // rule json, anchor due (y,m,d), expected next (y,m,d)
        { """{"type":"daily"}""", [2026, 6, 10], [2026, 6, 11] },
        { """{"type":"every_n_days","interval":3}""", [2026, 6, 10], [2026, 6, 13] }, // AS-05 — independent of completion date
        { """{"type":"weekdays","weekdays":[1,3]}""", [2026, 6, 8], [2026, 6, 10] },   // Mon → Wed (AS-03)
        { """{"type":"weekdays","weekdays":[1,3]}""", [2026, 6, 10], [2026, 6, 15] },  // Wed → next Mon (wraps)
        { """{"type":"weekdays","weekdays":[5]}""", [2026, 6, 12], [2026, 6, 19] },    // single day = weekly
        { """{"type":"weekdays","weekdays":[1,3]}""", [2026, 6, 9], [2026, 6, 10] },   // Tue (not listed) → Wed
        { """{"type":"monthly","dayOfMonth":31}""", [2026, 1, 31], [2026, 2, 28] },    // clamp
        { """{"type":"monthly","dayOfMonth":31}""", [2028, 1, 31], [2028, 2, 29] },    // leap clamp
        { """{"type":"monthly","dayOfMonth":31}""", [2026, 2, 28], [2026, 3, 31] },    // anchor restored after a clamped instance
        { """{"type":"monthly","dayOfMonth":15}""", [2026, 12, 15], [2027, 1, 15] },   // year wrap
        { """{"type":"every_n_days","interval":2}""", [2026, 3, 28], [2026, 3, 30] },  // across the spring DST switch: stays Warsaw midnight
        { """{"type":"daily"}""", [2026, 10, 24], [2026, 10, 25] },                     // across the autumn DST switch: stays Warsaw midnight
    };

    [Theory]
    [MemberData(nameof(DateOnlyFacts))]
    public void NextDueAfter_date_only_yields_the_first_occurrence_strictly_after_the_anchor_at_Warsaw_midnight(
        string ruleJson, int[] anchor, int[] expected)
    {
        var rule = RecurrenceRule.Parse(ruleJson);

        var next = rule.NextDueAfter(WarsawMidnight(anchor[0], anchor[1], anchor[2]), hasTime: false);

        next.Should().Be(WarsawMidnight(expected[0], expected[1], expected[2]));
        next.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void NextDueAfter_timed_keeps_the_Warsaw_wall_clock_across_the_spring_DST_switch()
    {
        var rule = new DailyRecurrence();

        // 2026-03-28 09:00 CET (08:00Z) → 2026-03-29 09:00 CEST (07:00Z): the wall-clock is preserved, the UTC offset moves.
        var next = rule.NextDueAfter(WarsawWallClock(2026, 3, 28, 9, 0), hasTime: true);

        next.Should().Be(new DateTime(2026, 3, 29, 7, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void NextDueAfter_timed_keeps_the_Warsaw_wall_clock_across_the_autumn_DST_switch()
    {
        var rule = new EveryNDaysRecurrence(7);

        // 2026-10-24 09:00 CEST (07:00Z) → 2026-10-31 09:00 CET (08:00Z).
        var next = rule.NextDueAfter(WarsawWallClock(2026, 10, 24, 9, 0), hasTime: true);

        next.Should().Be(new DateTime(2026, 10, 31, 8, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void NextDueAfter_is_a_pure_function_of_rule_and_anchor_never_of_the_clock()
    {
        // Completing on June 20 (or any day) does not change the result for an anchor of June 10 (ASM-09, Clarifications Q2).
        var rule = new EveryNDaysRecurrence(3);
        var anchor = WarsawMidnight(2026, 6, 10);

        rule.NextDueAfter(anchor, false).Should().Be(WarsawMidnight(2026, 6, 13));
        rule.NextDueAfter(anchor, false).Should().Be(rule.NextDueAfter(anchor, false));
    }
}
