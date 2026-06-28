using FluentAssertions;
using NodaTime;
using TaskFlow.Application.Time;

namespace TaskFlow.UnitTests.Time;

/// <summary>
/// The authoritative server-tier proof (T003/R13) for the <see cref="WarsawDayBounds"/> NodaTime seam:
/// the Warsaw calendar-day boundary is computed by the tzdb library, the UTC-midnight seam does not fall a
/// day early, and the spring-forward (23h) / fall-back (25h) days prove DST is library-driven, never
/// fixed-offset arithmetic (FR-092). All "now" values are frozen UTC instants — pure functions, deterministic.
/// </summary>
public sealed class WarsawDayBoundsTests
{
    private static DateTime Utc(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    [Fact]
    public void Start_of_today_and_tomorrow_are_midnight_Warsaw_in_summer_CEST()
    {
        // 2026-06-27 14:00 Warsaw (CEST, UTC+2). Day starts 00:00 CEST = 2026-06-26T22:00Z.
        var now = Utc(2026, 6, 27, 12, 0);

        WarsawDayBounds.StartOfTodayUtc(now).Should().Be(Utc(2026, 6, 26, 22, 0));
        WarsawDayBounds.StartOfTomorrowUtc(now).Should().Be(Utc(2026, 6, 27, 22, 0));
        WarsawDayBounds.StartOfDayPlusUtc(now, 8).Should().Be(Utc(2026, 7, 4, 22, 0));
    }

    [Fact]
    public void Returned_bounds_are_utc_kind()
    {
        var now = Utc(2026, 6, 27, 12, 0);

        WarsawDayBounds.StartOfTodayUtc(now).Kind.Should().Be(DateTimeKind.Utc);
        WarsawDayBounds.StartOfTomorrowUtc(now).Kind.Should().Be(DateTimeKind.Utc);
        WarsawDayBounds.StartOfDayPlusUtc(now, 8).Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Warsaw_local_date_is_the_Warsaw_calendar_date_not_the_truncated_UTC_date()
    {
        // 2026-06-27T21:30Z = 27 Jun 23:30 Warsaw → still the 27th (UTC date and Warsaw date agree).
        WarsawDayBounds.WarsawLocalDate(Utc(2026, 6, 27, 21, 30)).Should().Be(new LocalDate(2026, 6, 27));

        // 2026-06-27T22:30Z = 28 Jun 00:30 Warsaw → the 28th, NOT the UTC date 27th (the off-by-one this
        // seam exists to prevent — the UTC-midnight crossover).
        WarsawDayBounds.WarsawLocalDate(Utc(2026, 6, 27, 22, 30)).Should().Be(new LocalDate(2026, 6, 28));
    }

    [Fact]
    public void An_instant_past_the_UTC_midnight_seam_belongs_to_the_next_Warsaw_day()
    {
        // "now" = 2026-06-27T23:30Z = 28 Jun 01:30 Warsaw. Today-Warsaw is the 28th, so start-of-today is
        // the start of 28 Jun Warsaw (= 2026-06-27T22:00Z), NOT the start of 27 Jun.
        var now = Utc(2026, 6, 27, 23, 30);

        WarsawDayBounds.StartOfTodayUtc(now).Should().Be(Utc(2026, 6, 27, 22, 0));
        WarsawDayBounds.WarsawLocalDate(now).Should().Be(new LocalDate(2026, 6, 28));
    }

    [Fact]
    public void Spring_forward_day_is_23_hours_computed_by_the_tzdb_library()
    {
        // Warsaw springs forward 2026-03-29 02:00 CET → 03:00 CEST. Midnight 29 Mar is still CET (UTC+1),
        // so start-of-today = 2026-03-28T23:00Z; start-of-tomorrow (30 Mar, now CEST UTC+2) = 2026-03-29T22:00Z.
        var now = Utc(2026, 3, 29, 5, 0);

        var startToday = WarsawDayBounds.StartOfTodayUtc(now);
        var startTomorrow = WarsawDayBounds.StartOfTomorrowUtc(now);

        startToday.Should().Be(Utc(2026, 3, 28, 23, 0));
        startTomorrow.Should().Be(Utc(2026, 3, 29, 22, 0));
        (startTomorrow - startToday).Should().Be(TimeSpan.FromHours(23), "the spring-forward day is 23h — tzdb, not fixed-offset");
    }

    [Fact]
    public void Fall_back_day_is_25_hours_computed_by_the_tzdb_library()
    {
        // Warsaw falls back 2026-10-25 03:00 CEST → 02:00 CET. Midnight 25 Oct is still CEST (UTC+2), so
        // start-of-today = 2026-10-24T22:00Z; start-of-tomorrow (26 Oct, now CET UTC+1) = 2026-10-25T23:00Z.
        var now = Utc(2026, 10, 25, 5, 0);

        var startToday = WarsawDayBounds.StartOfTodayUtc(now);
        var startTomorrow = WarsawDayBounds.StartOfTomorrowUtc(now);

        startToday.Should().Be(Utc(2026, 10, 24, 22, 0));
        startTomorrow.Should().Be(Utc(2026, 10, 25, 23, 0));
        (startTomorrow - startToday).Should().Be(TimeSpan.FromHours(25), "the fall-back day is 25h — tzdb, not fixed-offset");
    }
}
