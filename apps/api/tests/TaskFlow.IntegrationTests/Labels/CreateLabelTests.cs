using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.IntegrationTests.Infrastructure;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.IntegrationTests.Labels;

/// <summary>
/// Allow + deny coverage for <c>PUT /api/labels/{id}</c> (operationId <c>createLabel</c>, slice 006).
/// Idempotent owner-scoped upsert; duplicate (owner, normalized name) → 422; deny-by-default 401 writes no row.
/// </summary>
public sealed class CreateLabelTests : SharingTestBase
{
    private static string LabelPath(Guid id) => $"/api/labels/{id}";

    [Fact]
    public async Task Allow_create_a_label_owned_by_the_caller()
    {
        var user = await CreateUserAsync("g-cl-o", "clo@example.com", "Owner");
        var id = Guid.CreateVersion7();

        using var response = await SendAsync(HttpMethod.Put, LabelPath(id), TokenFor(user), new { name = "Urgent", color = "red" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadLabelAsync();
        body.Id.Should().Be(id);
        body.Name.Should().Be("Urgent");
        body.Color.Should().Be("red");

        using var list = await SendAsync(HttpMethod.Get, "/api/labels", TokenFor(user));
        (await list.ReadLabelsAsync()).Should().ContainSingle(l => l.Id == id);
    }

    [Fact]
    public async Task Allow_idempotent_re_put_returns_existing_no_duplicate_row()
    {
        var user = await CreateUserAsync("g-cl-idem", "clidem@example.com", "Owner");
        var id = Guid.CreateVersion7();
        await SendAsync(HttpMethod.Put, LabelPath(id), TokenFor(user), new { name = "Work" });

        using var second = await SendAsync(HttpMethod.Put, LabelPath(id), TokenFor(user), new { name = "Work" });

        second.StatusCode.Should().Be(HttpStatusCode.OK, "a re-PUT of the same id is an idempotent replay");
        (await CountLabelsAsync()).Should().Be(1, "the idempotent replay writes no duplicate row");
    }

    [Fact]
    public async Task Deny_no_valid_session_is_401_and_writes_no_row()
    {
        // The silent-200 guard (slice-001 lesson): a body-only handler with no auth weave would 200 + write a
        // row. Assert exactly 401 + the RFC 9457 envelope + NO row written.
        var id = Guid.CreateVersion7();

        using var response = await SendAsync(HttpMethod.Put, LabelPath(id), TestJwtHelper.WrongKey("nobody"), new { name = "Urgent" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.MediaType().Should().Be("application/problem+json");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
        (await CountLabelsAsync()).Should().Be(0, "deny-by-default must weave — no row may be written");
    }

    [Fact]
    public async Task Deny_a_duplicate_name_is_422_case_insensitive()
    {
        var user = await CreateUserAsync("g-cl-dup", "cldup@example.com", "Owner");
        var token = TokenFor(user);
        await SendAsync(HttpMethod.Put, LabelPath(Guid.CreateVersion7()), token, new { name = "Work" });

        using var dup = await SendAsync(HttpMethod.Put, LabelPath(Guid.CreateVersion7()), token, new { name = "  work  " });

        dup.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "names are unique per owner, case-insensitive");
        (await dup.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
        (await CountLabelsAsync()).Should().Be(1, "the duplicate was rejected");
    }

    [Fact]
    public async Task Deny_a_non_preset_color_is_422()
    {
        var user = await CreateUserAsync("g-cl-color", "clcolor@example.com", "Owner");

        using var response = await SendAsync(HttpMethod.Put, LabelPath(Guid.CreateVersion7()), TokenFor(user),
            new { name = "Urgent", color = "#ff0000" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "color must be a preset token, not raw CSS");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
    }

    private async Task<int> CountLabelsAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Labels.CountAsync();
    }
}
