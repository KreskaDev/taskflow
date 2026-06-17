using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Domain.IdentityAccess.Events;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;
using Wolverine.Tracking;

namespace TaskFlow.IntegrationTests.IdentityAccess;

/// <summary>
/// Allow + deny coverage (SC-016, SC-017) for <c>DELETE /api/users/me</c> — irreversible account
/// erasure. The carrier <c>sub</c> is the caller's own TaskFlow user id (mirrors GetCurrentUser), so
/// "delete another user" is structurally impossible: a carrier always names itself. A carrier whose
/// sub no longer maps to a row (or is the tombstone) is rejected 401, never silently 204.
/// </summary>
/// <remarks>
/// RED-FIRST (Constitution VIII): this file compiles and fails until T049 (event), T050 (command +
/// handler) and T051 (DELETE route) land. The catastrophic primary assertion is the User-row
/// hard-delete (unconditionally observable). The event-dispatch assertion depends on the mechanism
/// scout A wires in T050 — see <c>Allow_dispatches_AccountDeletionRequested</c> below.
/// </remarks>
public sealed class DeleteAccountTests : IntegrationTestBase
{
    private const string MePath = "/api/users/me";
    private const string EnsurePath = "/api/users/ensure";

    private async Task<ProfileBody> CreateAccountAsync(string sub, string email)
    {
        return await (await SendAsync(
            HttpMethod.Post, EnsurePath, TestJwtHelper.Valid(sub),
            new { googleSubjectId = sub, email, displayName = "Delete Me", avatarUrl = (string?)null }))
            .ReadProfileAsync();
    }

    [Fact]
    public async Task Allow_hard_deletes_the_callers_row_leaving_no_residual_data()
    {
        var created = await CreateAccountAsync("google-sub-del-200", "del200@example.com");

        // The proxy carrier's sub is the caller's own TaskFlow user id.
        using var response = await SendAsync(HttpMethod.Delete, MePath, TestJwtHelper.Valid(created.Id.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // SC-017: the row is HARD-deleted (no soft-delete column), and only the seeded tombstone remains.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.AnyAsync(u => u.Id == UserId.From(created.Id)))
            .Should().BeFalse("account deletion hard-deletes the User row, leaving no residual row");
        (await db.Users.AnyAsync(u => u.Id == UserId.Tombstone))
            .Should().BeTrue("the anonymization tombstone is a separate seeded sentinel, never deleted");
    }

    [Fact]
    public async Task Allow_a_deleted_account_can_no_longer_authenticate()
    {
        var created = await CreateAccountAsync("google-sub-del-201", "del201@example.com");
        using (var del = await SendAsync(HttpMethod.Delete, MePath, TestJwtHelper.Valid(created.Id.ToString())))
        {
            del.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // The same carrier now references a row that no longer exists → 401 (SC-013 resolution).
        using var after = await SendAsync(HttpMethod.Get, MePath, TestJwtHelper.Valid(created.Id.ToString()));
        after.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await after.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
    }

    [Fact]
    public async Task Allow_dispatches_AccountDeletionRequested()
    {
        // AccountDeletionRequested is the FIRST published domain event. T050 makes it routable via a
        // durable local-queue route + no-op handler, so it is observable through the in-process
        // tracking harness. .Sent fires on enqueue (robust); the 204 + hard-delete are asserted by
        // Allow_hard_deletes_the_callers_row_leaving_no_residual_data.
        var created = await CreateAccountAsync("google-sub-del-202", "del202@example.com");

        var host = Services.GetRequiredService<IHost>();
        var tracked = await host.TrackActivity().Timeout(TimeSpan.FromSeconds(10)).ExecuteAndWaitAsync(
            _ => SendAsync(HttpMethod.Delete, MePath, TestJwtHelper.Valid(created.Id.ToString())));

        tracked.Sent.MessagesOf<AccountDeletionRequested>().Should().ContainSingle();
    }

    [Fact]
    public async Task Deny_no_jwt_is_rejected_401_with_our_envelope()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(MePath, UriKind.Relative));
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("unauthenticated");
        problem.Status.Should().Be(401);
    }

    [Fact]
    public async Task Deny_invalid_signature_is_rejected_401()
    {
        using var response = await SendAsync(HttpMethod.Delete, MePath, TestJwtHelper.WrongKey(Guid.NewGuid().ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Deny_expired_jwt_is_rejected_401()
    {
        using var response = await SendAsync(HttpMethod.Delete, MePath, TestJwtHelper.Expired(Guid.NewGuid().ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Deny_a_valid_jwt_for_a_nonexistent_user_is_rejected_401()
    {
        // "Cannot delete another user": a carrier always names ITSELF (sub = own id). A well-formed
        // carrier whose sub maps to no row must be rejected, never silently succeed (idempotent 204).
        var ghost = Guid.NewGuid().ToString();

        using var response = await SendAsync(HttpMethod.Delete, MePath, TestJwtHelper.Valid(ghost));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
    }

    [Fact]
    public async Task Deny_the_tombstone_identity_is_rejected_401()
    {
        using var response = await SendAsync(HttpMethod.Delete, MePath, TestJwtHelper.Valid(Guid.Empty.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
    }
}
