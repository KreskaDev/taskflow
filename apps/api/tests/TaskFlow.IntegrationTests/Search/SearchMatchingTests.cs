using System.Net;
using FluentAssertions;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests.Search;

/// <summary>
/// contracts/search-api.md §Matching (slice 013, T002): title + description hits, case-insensitivity, token-AND,
/// projects/labels by name, the blank/over-long query boundary, literal LIKE metacharacters, ranking and caps.
/// </summary>
public sealed class SearchMatchingTests : SearchTestBase
{
    private async Task<(UserId User, string Token)> NewUserAsync(string slug)
    {
        var user = await CreateUserAsync($"g-{slug}", $"{slug}@example.com", "Owner");
        return (user, TokenFor(user));
    }

    [Fact]
    public async Task Finds_tasks_by_title_and_by_description_case_insensitively()
    {
        var (user, token) = await NewUserAsync("match-basic");
        var byTitle = await SeedSearchTaskAsync(user, "Kup mleko");
        var byDescription = await SeedSearchTaskAsync(user, "Zakupy", description: "pamiętaj o MLEKU i mleko bez laktozy", position: "a1");
        await SeedSearchTaskAsync(user, "Umyj okna", position: "a2");

        var result = await SearchAsync(token, "MLEKO");

        result.Tasks.Select(t => t.Id).Should().BeEquivalentTo([byTitle, byDescription]);
        result.Projects.Should().BeEmpty();
        result.Labels.Should().BeEmpty();
    }

    [Fact]
    public async Task Token_AND_every_token_must_match_somewhere_in_the_row()
    {
        var (user, token) = await NewUserAsync("match-and");
        var kupMleko = await SeedSearchTaskAsync(user, "Kup mleko");
        await SeedSearchTaskAsync(user, "Kup ser", position: "a1");

        (await SearchAsync(token, "kup mleko")).Tasks.Select(t => t.Id).Should().Equal(kupMleko);
        (await SearchAsync(token, "kup ser mleko")).Tasks.Should().BeEmpty();
        (await SearchAsync(token, "kup")).Tasks.Should().HaveCount(2);
    }

    [Fact]
    public async Task Finds_projects_and_labels_by_name()
    {
        var (user, token) = await NewUserAsync("match-pl");
        var project = await CreateProjectAsync(token, "Remont łazienki");
        await CreateProjectAsync(token, "Praca");
        var label = await SeedLabelAsync(user, "remont");
        await SeedLabelAsync(user, "dom");

        var result = await SearchAsync(token, "remont");

        result.Projects.Select(p => p.Id).Should().Equal(project.Id);
        result.Labels.Select(l => l.Id).Should().Equal(label);
        result.Tasks.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_query_returns_three_empty_buckets(string q)
    {
        var (user, token) = await NewUserAsync($"match-blank-{q.Length}");
        await SeedSearchTaskAsync(user, "Cokolwiek");

        var result = await SearchAsync(token, q);

        result.Tasks.Should().BeEmpty();
        result.Projects.Should().BeEmpty();
        result.Labels.Should().BeEmpty();
    }

    [Fact]
    public async Task A_query_over_200_characters_is_422()
    {
        var (_, token) = await NewUserAsync("match-long");

        using var response = await SendSearchAsync(token, new string('a', 201));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
    }

    [Fact]
    public async Task LIKE_metacharacters_are_literal_not_wildcards()
    {
        var (user, token) = await NewUserAsync("match-meta");
        var percent = await SeedSearchTaskAsync(user, "Rabat 50% na mleko");
        await SeedSearchTaskAsync(user, "Rabat 50 na mleko", position: "a1");
        var underscore = await SeedSearchTaskAsync(user, "snake_case", position: "a2");
        await SeedSearchTaskAsync(user, "snakeXcase", position: "a3");

        (await SearchAsync(token, "50")).Tasks.Should().HaveCount(2, "plain substring sanity before the escaped case");
        (await SearchAsync(token, "snake_case")).Tasks.Select(t => t.Id).Should().Equal(underscore);
        (await SearchAsync(token, "50%")).Tasks.Select(t => t.Id).Should().Equal(percent);
    }

    [Fact]
    public async Task Ranking_exact_then_prefix_then_substring_and_open_before_done()
    {
        var (user, token) = await NewUserAsync("match-rank");
        var substring = await SeedSearchTaskAsync(user, "Kup mleko", position: "a0");
        var prefix = await SeedSearchTaskAsync(user, "Mleko sojowe", position: "a1");
        var exact = await SeedSearchTaskAsync(user, "Mleko", position: "a2");
        var doneExact = await SeedSearchTaskAsync(user, "mleko", status: "done", position: "a3");

        var result = await SearchAsync(token, "mleko");

        result.Tasks.Select(t => t.Id).Should().Equal(exact, doneExact, prefix, substring);
    }

    [Fact]
    public async Task Archived_projects_rank_after_live_ones()
    {
        var (_, token) = await NewUserAsync("match-arch");
        var archived = await CreateProjectAsync(token, "Alfa projekt");
        await ArchiveProjectViaApiAsync(token, archived);
        var live = await CreateProjectAsync(token, "Beta projekt");

        var result = await SearchAsync(token, "projekt");

        result.Projects.Select(p => p.Id).Should().Equal(live.Id, archived.Id);
        result.Projects[^1].ArchivedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Task_bucket_is_capped_at_twenty()
    {
        var (user, token) = await NewUserAsync("match-cap");
        for (var i = 0; i < 25; i++)
        {
            await SeedSearchTaskAsync(user, $"Cap mleko {i:D2}", position: $"a{i:D2}");
        }

        var result = await SearchAsync(token, "mleko");

        result.Tasks.Should().HaveCount(20);
    }

    [Fact]
    public async Task Unauthenticated_search_is_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, SearchPath("mleko"));
        using var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
