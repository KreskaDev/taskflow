using System.Net;
using FluentAssertions;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests.Labels;

/// <summary>
/// Allow + deny coverage for <c>PATCH /api/labels/{id}</c> (operationId <c>updateLabel</c>, slice 006).
/// Whole-object rename + recolor; ownership-gated (not-owned/absent → 404); duplicate name → 422.
/// </summary>
public sealed class UpdateLabelTests : SharingTestBase
{
    private static string LabelPath(Guid id) => $"/api/labels/{id}";

    private async Task<Guid> CreateLabelAsync(string token, string name, string? color = null)
    {
        var id = Guid.CreateVersion7();
        using var response = await SendAsync(HttpMethod.Put, LabelPath(id), token, new { name, color });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return id;
    }

    [Fact]
    public async Task Allow_rename_and_recolor()
    {
        var user = await CreateUserAsync("g-ul-o", "ulo@example.com", "Owner");
        var token = TokenFor(user);
        var id = await CreateLabelAsync(token, "Old", "red");

        using var response = await SendAsync(HttpMethod.Patch, LabelPath(id), token, new { name = "New", color = "blue" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadLabelAsync();
        body.Name.Should().Be("New");
        body.Color.Should().Be("blue");
    }

    [Fact]
    public async Task Allow_clearing_the_color()
    {
        var user = await CreateUserAsync("g-ul-clr", "ulclr@example.com", "Owner");
        var token = TokenFor(user);
        var id = await CreateLabelAsync(token, "Tag", "red");

        using var response = await SendAsync(HttpMethod.Patch, LabelPath(id), token, new { name = "Tag", color = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadLabelAsync()).Color.Should().BeNull();
    }

    [Fact]
    public async Task Deny_a_label_not_owned_is_404()
    {
        var alice = await CreateUserAsync("g-ul-a", "ula@example.com", "Alice");
        var bob = await CreateUserAsync("g-ul-b", "ulb@example.com", "Bob");
        var id = await CreateLabelAsync(TokenFor(alice), "AliceLabel");

        using var response = await SendAsync(HttpMethod.Patch, LabelPath(id), TokenFor(bob), new { name = "Hijack" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "another user's label is not observable (uniform existence-hide)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_an_absent_label_is_404()
    {
        var user = await CreateUserAsync("g-ul-abs", "ulabs@example.com", "Owner");

        using var response = await SendAsync(HttpMethod.Patch, LabelPath(Guid.CreateVersion7()), TokenFor(user), new { name = "Ghost" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_a_duplicate_name_is_422()
    {
        var user = await CreateUserAsync("g-ul-dup", "uldup@example.com", "Owner");
        var token = TokenFor(user);
        await CreateLabelAsync(token, "Work");
        var id = await CreateLabelAsync(token, "Home");

        using var response = await SendAsync(HttpMethod.Patch, LabelPath(id), token, new { name = "work" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "renaming onto an existing name collides");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task Allow_renaming_to_the_same_name_is_not_a_self_collision()
    {
        var user = await CreateUserAsync("g-ul-self", "ulself@example.com", "Owner");
        var token = TokenFor(user);
        var id = await CreateLabelAsync(token, "Work", "red");

        using var response = await SendAsync(HttpMethod.Patch, LabelPath(id), token, new { name = "Work", color = "blue" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the duplicate pre-check excludes the label itself");
        (await response.ReadLabelAsync()).Color.Should().Be("blue");
    }
}
