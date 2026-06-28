using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// The server-tier DST-boundary proof (T014/T016/R13) for Today + Upcoming. The clock is frozen to the
/// Warsaw spring-forward (2026-03-29 05:00Z — the day Warsaw jumps 02:00 CET → 03:00 CEST, a 23-hour day).
/// The membership/grouping assertions DISCRIMINATE the tzdb boundary from fixed-offset arithmetic: with the
/// correct 23h day, start-of-tomorrow is 2026-03-29T22:00Z, so a task at 22:30Z is the NEXT Warsaw day; a
/// naive fixed UTC+1 would put start-of-tomorrow at 23:00Z and wrongly keep that task in Today.
/// </summary>
public sealed class DailyViewsDstIntegrationTests : SharingTestBase
{
    private static readonly DateTimeOffset FrozenNow = new(2026, 3, 29, 5, 0, 0, TimeSpan.Zero);
    private static readonly DateTime InToday = new(2026, 3, 29, 21, 30, 0, DateTimeKind.Utc);  // 23:30 Warsaw, the 29th
    private static readonly DateTime NextDay = new(2026, 3, 29, 22, 30, 0, DateTimeKind.Utc);  // 00:30 Warsaw, the 30th

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(FrozenNow));
    }

    [Fact]
    public async Task Today_membership_uses_the_tzdb_23h_spring_forward_day_not_a_fixed_offset()
    {
        var owner = await CreateUserAsync("g-dst-t", "dstt@example.com", "Owner");
        var inToday = await SeedTaskAsync(owner, "23:30 Warsaw 29th", "a0", dueDate: InToday, dueHasTime: true);
        var nextDay = await SeedTaskAsync(owner, "00:30 Warsaw 30th", "a1", dueDate: NextDay, dueHasTime: true);

        using var response = await SendAsync(HttpMethod.Get, "/api/tasks/today", TokenFor(owner));

        var ids = (await response.ReadTodayAsync()).Groups.SelectMany(g => g.Tasks).Select(t => t.Id).ToList();
        ids.Should().Contain(inToday);
        ids.Should().NotContain(nextDay, "start-of-tomorrow is 22:00Z (the 23h DST day) — a fixed UTC+1 would wrongly keep this in Today");
    }

    [Fact]
    public async Task Upcoming_groups_the_next_day_by_its_Warsaw_date_across_the_dst_seam()
    {
        var owner = await CreateUserAsync("g-dst-u", "dstu@example.com", "Owner");
        var nextDay = await SeedTaskAsync(owner, "00:30 Warsaw 30th", "a0", dueDate: NextDay, dueHasTime: true);

        using var response = await SendAsync(HttpMethod.Get, "/api/tasks/upcoming", TokenFor(owner));

        var groups = (await response.ReadUpcomingAsync()).Groups;
        groups.Should().ContainSingle(g => g.Date == "2026-03-30", "the 00:30 Warsaw instant groups under the Warsaw date 2026-03-30");
        groups.Single().Tasks.Single().Id.Should().Be(nextDay);
    }
}
