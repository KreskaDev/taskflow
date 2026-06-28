namespace TaskFlow.Application.TaskManagement;

/// <summary>
/// The three slice-003 due-date trust-boundary rules (research R8/R11/R13), extracted from
/// <c>CreateTaskValidator</c> so the slice-005 reschedule/edit commands reuse the IDENTICAL semantics and
/// messages — both trust boundaries stay in lockstep (Constitution VI). All three are zone-agnostic UTC
/// checks (no NodaTime here; the Warsaw boundary lives only in <c>WarsawDayBounds</c>).
/// </summary>
public static class DueDateRules
{
    /// <summary>The earliest plausible due-date instant — a wide sanity floor, not business logic (R11).</summary>
    private static readonly DateTime MinDue = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The plausible-range horizon: ~10 years beyond now (R11). A UTC comparison, zone-agnostic.</summary>
    private const int MaxDueYearsAhead = 10;

    /// <summary>Message for a half-set <c>{DueDate, DueHasTime}</c> pair.</summary>
    public const string PairingMessage = "DueDate and DueHasTime must be set together (both present or both absent).";

    /// <summary>Message for a non-UTC-kind due instant.</summary>
    public const string UtcKindMessage = "DueDate must be a UTC instant (an ISO-8601 'Z' form).";

    /// <summary>Message for an implausible (corrupt/absurd) due instant.</summary>
    public const string RangeMessage = "DueDate is outside the plausible range.";

    /// <summary>
    /// The pairing invariant (R8): both null (no due date) or both non-null. A PRESENCE check —
    /// <c>DueHasTime=false</c> is "present" (date-only), so compare <c>HasValue</c>, never truthiness.
    /// </summary>
    public static bool IsPairingConsistent(DateTime? dueDate, bool? dueHasTime) =>
        dueDate.HasValue == dueHasTime.HasValue;

    /// <summary>
    /// The UTC-kind guard (R13): a resolved instant MUST be a Z-form UTC <see cref="DateTime"/> — a non-UTC
    /// kind would make Npgsql throw an unhandled 500 against the <c>timestamptz</c> column, so reject as 422.
    /// </summary>
    public static bool IsUtcKindOrAbsent(DateTime? dueDate) =>
        !dueDate.HasValue || dueDate.Value.Kind == DateTimeKind.Utc;

    /// <summary>The plausible-range sanity window (R11): reject corrupt/absurd instants. Zone-agnostic UTC compare.</summary>
    public static bool IsWithinPlausibleRange(DateTime? dueDate)
    {
        if (!dueDate.HasValue || dueDate.Value.Kind != DateTimeKind.Utc)
        {
            // No due date (passes) or wrong kind (the UTC-kind rule owns that failure — don't double-fail on
            // a kind we can't meaningfully range-compare).
            return true;
        }

        return dueDate.Value >= MinDue && dueDate.Value <= DateTime.UtcNow.AddYears(MaxDueYearsAhead);
    }
}
