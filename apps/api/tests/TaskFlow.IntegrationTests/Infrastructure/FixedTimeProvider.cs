namespace TaskFlow.IntegrationTests.Infrastructure;

/// <summary>
/// A frozen <see cref="TimeProvider"/> for deterministic Today/Upcoming Warsaw day-boundary tests (slice
/// 005). Registered per test class via <c>IntegrationTestBase.ConfigureTestServices</c> so the query handlers
/// compute the boundary against a fixed "now" — the membership, overdue, and DST-boundary assertions are then
/// reproducible regardless of when the suite runs.
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
