using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;

namespace TaskFlow.Domain.TaskManagement;

/// <summary>
/// ENT-05 — the recurrence rule of a <see cref="Task"/> (slice 012, FR-007, research D1/D3). A closed
/// union of four frequency types persisted as camelCase JSON in <c>tasks.recurrence_rule</c> (jsonb):
/// <c>{"type":"daily"}</c>, <c>{"type":"every_n_days","interval":N}</c>,
/// <c>{"type":"weekdays","weekdays":[1..7]}</c> (ISO Monday=1 … Sunday=7), and
/// <c>{"type":"monthly","dayOfMonth":D}</c> — where <c>dayOfMonth</c> is the ANCHOR day that survives
/// end-of-month clamping (Jan 31 → Feb 28 → Mar 31).
/// </summary>
/// <remarks>
/// <see cref="NextDueAfter"/> is a pure function of (rule, due): it yields the FIRST occurrence strictly
/// after the anchor due date in <c>Europe/Warsaw</c> (FR-092, ASM-09) and never skips occurrences
/// (Clarifications Q2). Date-only dues keep the Warsaw-midnight convention; timed dues keep the same
/// Warsaw wall-clock time across DST (<c>AtLeniently</c> — tzdb, never offset arithmetic).
/// </remarks>
public abstract record RecurrenceRule
{
    /// <summary>The smallest legal "every N days" interval (interval 1 is spelled <see cref="DailyRecurrence"/>).</summary>
    public const int MinInterval = 2;

    /// <summary>The largest legal "every N days" interval.</summary>
    public const int MaxInterval = 365;

    private static readonly DateTimeZone Zone = DateTimeZoneProviders.Tzdb["Europe/Warsaw"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private protected RecurrenceRule()
    {
    }

    /// <summary>The wire/storage discriminator (<c>daily | every_n_days | weekdays | monthly</c>).</summary>
    public abstract string Type { get; }

    /// <summary>
    /// Builds a rule from its wire parts, validating the closed set (D1). Members not belonging to the
    /// type must be absent; out-of-range values throw. Used by the Application DTO mapping after
    /// FluentValidation has already produced the friendly 422.
    /// </summary>
    /// <param name="type">The discriminator.</param>
    /// <param name="interval">Required for <c>every_n_days</c>, otherwise null.</param>
    /// <param name="weekdays">Required for <c>weekdays</c>, otherwise null.</param>
    /// <param name="dayOfMonth">Required for <c>monthly</c>, otherwise null.</param>
    public static RecurrenceRule Create(string type, int? interval, IReadOnlyCollection<int>? weekdays, int? dayOfMonth)
    {
        ArgumentNullException.ThrowIfNull(type);
        switch (type)
        {
            case "daily":
                RequireAbsent(interval, weekdays, dayOfMonth, type);
                return new DailyRecurrence();
            case "every_n_days":
                RequireAbsent(null, weekdays, dayOfMonth, type);
                return new EveryNDaysRecurrence(interval ?? throw new ArgumentException("every_n_days requires 'interval'.", nameof(interval)));
            case "weekdays":
                RequireAbsent(interval, null, dayOfMonth, type);
                return new WeekdaysRecurrence(weekdays ?? throw new ArgumentException("weekdays requires 'weekdays'.", nameof(weekdays)));
            case "monthly":
                RequireAbsent(interval, weekdays, null, type);
                return new MonthlyRecurrence(dayOfMonth ?? throw new ArgumentException("monthly requires 'dayOfMonth'.", nameof(dayOfMonth)));
            default:
                throw new ArgumentException($"Unknown recurrence type '{type}'.", nameof(type));
        }
    }

    /// <summary>Parses the persisted JSON (jsonb column). Invalid documents throw — never silently coerced (Constitution VI).</summary>
    /// <param name="json">The stored JSON document.</param>
    public static RecurrenceRule Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        WireShape shape;
        try
        {
            shape = JsonSerializer.Deserialize<WireShape>(json, JsonOptions)
                ?? throw new ArgumentException("Recurrence rule JSON must be an object.", nameof(json));
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Recurrence rule JSON is malformed.", nameof(json), ex);
        }

        if (shape.Type is null)
        {
            throw new ArgumentException("Recurrence rule JSON must carry a 'type'.", nameof(json));
        }

        return Create(shape.Type, shape.Interval, shape.Weekdays, shape.DayOfMonth);
    }

    /// <summary>Serializes the rule to its camelCase storage/wire JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(
        new WireShape
        {
            Type = Type,
            Interval = (this as EveryNDaysRecurrence)?.Interval,
            Weekdays = (this as WeekdaysRecurrence)?.Days.ToArray(),
            DayOfMonth = (this as MonthlyRecurrence)?.DayOfMonth,
        },
        JsonOptions);

    /// <summary>
    /// The FIRST occurrence strictly after <paramref name="dueUtc"/>, evaluated in Europe/Warsaw (D3).
    /// </summary>
    /// <param name="dueUtc">The anchor due instant (UTC).</param>
    /// <param name="hasTime">Whether the due carries a wall-clock time (kept) or is date-only (Warsaw midnight).</param>
    public DateTime NextDueAfter(DateTime dueUtc, bool hasTime)
    {
        var instant = Instant.FromDateTimeUtc(dueUtc.Kind == DateTimeKind.Utc ? dueUtc : DateTime.SpecifyKind(dueUtc, DateTimeKind.Utc));
        var zoned = instant.InZone(Zone);
        var date = zoned.Date;

        var next = this switch
        {
            DailyRecurrence => date.PlusDays(1),
            EveryNDaysRecurrence n => date.PlusDays(n.Interval),
            WeekdaysRecurrence w => NextListedWeekday(date, w.Days),
            MonthlyRecurrence m => NextMonthClamped(date, m.DayOfMonth),
            _ => throw new InvalidOperationException("Unknown recurrence rule type."),
        };

        var zonedNext = hasTime
            ? Zone.AtLeniently(next.At(zoned.TimeOfDay))
            : Zone.AtStartOfDay(next);
        return zonedNext.ToInstant().ToDateTimeUtc();
    }

    private static LocalDate NextListedWeekday(LocalDate date, IReadOnlyList<int> days)
    {
        for (var offset = 1; offset <= 7; offset++)
        {
            var candidate = date.PlusDays(offset);
            if (days.Contains((int)candidate.DayOfWeek))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("A non-empty weekday set always yields a next day within 7 days.");
    }

    private static LocalDate NextMonthClamped(LocalDate date, int dayOfMonth)
    {
        var firstOfNext = new LocalDate(date.Year, date.Month, 1).PlusMonths(1);
        var daysInMonth = CalendarSystem.Iso.GetDaysInMonth(firstOfNext.Year, firstOfNext.Month);
        return new LocalDate(firstOfNext.Year, firstOfNext.Month, Math.Min(dayOfMonth, daysInMonth));
    }

    private static void RequireAbsent(int? interval, IReadOnlyCollection<int>? weekdays, int? dayOfMonth, string type)
    {
        if (interval is not null || weekdays is not null || dayOfMonth is not null)
        {
            throw new ArgumentException($"Recurrence type '{type}' does not accept the supplied members.", nameof(type));
        }
    }

    private sealed class WireShape
    {
        public string? Type { get; set; }

        public int? Interval { get; set; }

        public int[]? Weekdays { get; set; }

        public int? DayOfMonth { get; set; }
    }
}

/// <summary>Repeats every day.</summary>
public sealed record DailyRecurrence : RecurrenceRule
{
    public override string Type => "daily";
}

/// <summary>Repeats every <see cref="Interval"/> days (2..365).</summary>
public sealed record EveryNDaysRecurrence : RecurrenceRule
{
    public EveryNDaysRecurrence(int interval)
    {
        if (interval is < MinInterval or > MaxInterval)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, $"Interval must be between {MinInterval} and {MaxInterval}.");
        }

        Interval = interval;
    }

    public int Interval { get; }

    public override string Type => "every_n_days";
}

/// <summary>Repeats on the listed ISO weekdays (Monday=1 … Sunday=7; non-empty, distinct, ascending).</summary>
public sealed record WeekdaysRecurrence : RecurrenceRule
{
    private readonly int[] _days;

    public WeekdaysRecurrence(IReadOnlyCollection<int> days)
    {
        ArgumentNullException.ThrowIfNull(days);
        if (days.Count == 0)
        {
            throw new ArgumentException("At least one weekday is required.", nameof(days));
        }

        if (days.Any(d => d is < 1 or > 7))
        {
            throw new ArgumentOutOfRangeException(nameof(days), "Weekdays must be ISO numbers 1 (Monday) through 7 (Sunday).");
        }

        if (days.Distinct().Count() != days.Count)
        {
            throw new ArgumentException("Weekdays must be distinct.", nameof(days));
        }

        _days = days.OrderBy(d => d).ToArray();
    }

    /// <summary>The ISO weekday numbers, ascending.</summary>
    public IReadOnlyList<int> Days => _days;

    public override string Type => "weekdays";

    public bool Equals(WeekdaysRecurrence? other) => other is not null && _days.AsSpan().SequenceEqual(other._days);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var d in _days)
        {
            hash.Add(d);
        }

        return hash.ToHashCode();
    }
}

/// <summary>Repeats monthly on <see cref="DayOfMonth"/> (1..31), clamped to the target month's length.</summary>
public sealed record MonthlyRecurrence : RecurrenceRule
{
    public MonthlyRecurrence(int dayOfMonth)
    {
        if (dayOfMonth is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(dayOfMonth), dayOfMonth, "Day of month must be between 1 and 31.");
        }

        DayOfMonth = dayOfMonth;
    }

    public int DayOfMonth { get; }

    public override string Type => "monthly";
}

