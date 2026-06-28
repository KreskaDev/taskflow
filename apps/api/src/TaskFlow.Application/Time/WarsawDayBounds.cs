using NodaTime;

namespace TaskFlow.Application.Time;

/// <summary>
/// The ONE server-side seam for the <c>Europe/Warsaw</c> calendar-day boundary (research R1, data-model §2).
/// Every date-relative computation this slice owns — Today/Upcoming membership — derives its UTC bounds
/// here, mirroring the client's <c>apps/web/src/lib/timezone.ts</c> so the FR-092 "applied identically on
/// client and server" rule holds. DST is handled by the tzdb library (NodaTime), never fixed-offset
/// arithmetic.
/// </summary>
/// <remarks>
/// <para>NodaTime is confined to this helper: it takes a UTC <see cref="DateTime"/> "now" and emits plain
/// UTC <see cref="DateTime"/> bounds (<see cref="DateTimeKind.Utc"/>), after which the SQL range filter is
/// zone-free (<c>due_date &gt;= @lo AND due_date &lt; @hi</c>). The <c>Instant ↔ DateTime(UTC)</c> boundary
/// is crossed inside the helper; nothing downstream sees a NodaTime type. No column is remapped and the
/// <c>Npgsql.NodaTime</c> plugin is deliberately NOT adopted (no migration — research R1/R9).</para>
/// <para>The functions are pure (the caller supplies "now"), so the unit tests (T003) freeze "now" — the
/// authoritative server-tier proof of the DST and UTC-midnight-seam handling.</para>
/// </remarks>
public static class WarsawDayBounds
{
    /// <summary>The instance reference timezone (ASM-12 / FR-092). Resolved once from the tzdb provider.</summary>
    private static readonly DateTimeZone Zone = DateTimeZoneProviders.Tzdb["Europe/Warsaw"];

    /// <summary>The UTC instant at the start of <b>today</b> in <c>Europe/Warsaw</c>, relative to <paramref name="utcNow"/>.</summary>
    public static DateTime StartOfTodayUtc(DateTime utcNow) => StartOfDayPlusUtc(utcNow, 0);

    /// <summary>The UTC instant at the start of <b>tomorrow</b> in <c>Europe/Warsaw</c> — the Today/Upcoming split point.</summary>
    public static DateTime StartOfTomorrowUtc(DateTime utcNow) => StartOfDayPlusUtc(utcNow, 1);

    /// <summary>
    /// The UTC instant at the start of the Warsaw calendar day <paramref name="days"/> after the day
    /// containing <paramref name="utcNow"/> (e.g. <c>8</c> for the Upcoming upper bound). DST-correct: a
    /// day-start that does not exist on a spring-forward transition resolves to the first valid instant.
    /// </summary>
    public static DateTime StartOfDayPlusUtc(DateTime utcNow, int days)
    {
        var today = ToInstant(utcNow).InZone(Zone).Date;
        var target = today.PlusDays(days);
        return Zone.AtStartOfDay(target).ToInstant().ToDateTimeUtc();
    }

    /// <summary>
    /// The <b>Warsaw</b> calendar date (<see cref="LocalDate"/>) the UTC instant <paramref name="utcInstant"/>
    /// falls on — the Upcoming group key (R3/§6). NOT the truncated UTC date (the off-by-one across the
    /// Warsaw/UTC offset this slice exists to prevent).
    /// </summary>
    public static LocalDate WarsawLocalDate(DateTime utcInstant) => ToInstant(utcInstant).InZone(Zone).Date;

    private static Instant ToInstant(DateTime utc) =>
        // Treat the input as a UTC instant. EF reads timestamptz back with Kind=Utc and DateTime.UtcNow is
        // Utc; SpecifyKind normalizes a test-supplied Unspecified value so FromDateTimeUtc never throws.
        Instant.FromDateTimeUtc(utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc));
}
