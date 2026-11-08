using System.Diagnostics;
using FluentAssertions;
using TaskFlow.Domain.IdentityAccess;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.Search;

/// <summary>
/// contracts/search-api.md §Access (slice 013, T003 — SC-013/SC-016): results are dispatched by each candidate's
/// visibility — ownership for personal items, CURRENT membership for shared ones (viewer suffices for a read), a
/// former member gets nothing, labels are per-user.
/// </summary>
public sealed class SearchAccessTests : SearchTestBase
{
    [Fact]
    public async Task Another_users_personal_task_is_never_returned()
    {
        var owner = await CreateUserAsync("g-acc-owner", "acc-owner@example.com", "Owner");
        var stranger = await CreateUserAsync("g-acc-stranger", "acc-stranger@example.com", "Stranger");
        var mine = await SeedSearchTaskAsync(owner, "Prywatne mleko");
        await SeedSearchTaskAsync(stranger, "Cudze mleko");

        var result = await SearchAsync(TokenFor(owner), "mleko");

        result.Tasks.Select(t => t.Id).Should().Equal(mine);
    }

    [Fact]
    public async Task Shared_project_member_viewer_sees_the_project_and_its_tasks_non_member_sees_neither()
    {
        var owner = await CreateUserAsync("g-acc-o", "acc-o@example.com", "Owner");
        var viewer = await CreateUserAsync("g-acc-v", "acc-v@example.com", "Viewer");
        var outsider = await CreateUserAsync("g-acc-x", "acc-x@example.com", "Outsider");
        var ownerToken = TokenFor(owner);
        var project = await ShareProjectAsync(ownerToken, await CreateProjectAsync(ownerToken, "Wspólne mleko"));
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);
        var ownersTask = await SeedSearchTaskAsync(owner, "Mleko dla zespołu", projectId: project.Id);

        var asViewer = await SearchAsync(TokenFor(viewer), "mleko");
        asViewer.Projects.Select(p => p.Id).Should().Equal(project.Id);
        asViewer.Projects[0].Role.Should().Be("viewer");
        asViewer.Tasks.Select(t => t.Id).Should().Equal(ownersTask); // a viewer reads the owner's shared task (FR-067: read)

        var asOutsider = await SearchAsync(TokenFor(outsider), "mleko");
        asOutsider.Projects.Should().BeEmpty();
        asOutsider.Tasks.Should().BeEmpty();
    }

    [Fact]
    public async Task Former_member_sees_nothing_even_as_creator_and_assignee()
    {
        var owner = await CreateUserAsync("g-acc-fo", "acc-fo@example.com", "Owner");
        var former = await CreateUserAsync("g-acc-ff", "acc-ff@example.com", "Former");
        var ownerToken = TokenFor(owner);
        var project = await ShareProjectAsync(ownerToken, await CreateProjectAsync(ownerToken, "Projekt mleczny"));
        await SeedMembershipAsync(project.Id, former, MembershipRoles.Editor);
        var createdByFormer = await SeedSearchTaskAsync(former, "Mleko od byłego", projectId: project.Id);
        using (var assigned = await SendAsync(HttpMethod.Patch, $"/api/tasks/{createdByFormer}/assignees", ownerToken,
            new { assigneeIds = new[] { former.Value }, version = (await LoadTaskAsync(createdByFormer))!.Version }))
        {
            assigned.EnsureSuccessStatusCode();
        }

        (await SearchAsync(TokenFor(former), "mleko")).Tasks.Should().HaveCount(1, "still a member");

        await DeleteMembershipRowAsync(project.Id, former);

        var afterLeaving = await SearchAsync(TokenFor(former), "mleko");
        afterLeaving.Projects.Should().BeEmpty("membership loss revokes ALL access (FR-066)");
        afterLeaving.Tasks.Should().BeEmpty("createdBy/assignee are provenance only");
    }

    [Fact]
    public async Task Archived_shared_project_and_its_tasks_remain_searchable_for_members()
    {
        var owner = await CreateUserAsync("g-acc-ao", "acc-ao@example.com", "Owner");
        var member = await CreateUserAsync("g-acc-am", "acc-am@example.com", "Member");
        var ownerToken = TokenFor(owner);
        var project = await ShareProjectAsync(ownerToken, await CreateProjectAsync(ownerToken, "Archiwalne mleko"));
        await SeedMembershipAsync(project.Id, member, MembershipRoles.Editor);
        var task = await SeedSearchTaskAsync(owner, "Mleko w archiwum", projectId: project.Id);
        await ArchiveProjectViaApiAsync(ownerToken, project with { Version = project.Version });

        var result = await SearchAsync(TokenFor(member), "mleko");

        result.Projects.Select(p => p.Id).Should().Equal(project.Id);
        result.Projects[0].ArchivedAt.Should().NotBeNull("US-10.AS-06: archived projects are results");
        result.Tasks.Select(t => t.Id).Should().Equal(task);
    }

    [Fact]
    public async Task Another_users_label_is_never_returned()
    {
        var me = await CreateUserAsync("g-acc-lm", "acc-lm@example.com", "Me");
        var other = await CreateUserAsync("g-acc-lo", "acc-lo@example.com", "Other");
        var mine = await SeedLabelAsync(me, "mleczne");
        await SeedLabelAsync(other, "mleczne");

        var result = await SearchAsync(TokenFor(me), "mlecz");

        result.Labels.Select(l => l.Id).Should().Equal(mine);
    }

    /// <summary>
    /// SC-009 characterization: 10 000 personal tasks; the measured server round-trip is logged and held under a
    /// generous CI ceiling (the 50 ms product figure is an informational target on CI hardware).
    /// </summary>
    [Fact]
    public async Task Ten_thousand_tasks_search_stays_within_the_ci_ceiling()
    {
        var user = await CreateUserAsync("g-acc-10k", "acc-10k@example.com", "Heavy");
        await SeedManyTasksAsync(user, 10_000, "Zadanie masowe");
        var needle = await SeedSearchTaskAsync(user, "Kup mleko wśród tysięcy", position: "zz");
        (await CountTasksAsync(user)).Should().BeGreaterThanOrEqualTo(10_001);
        var token = TokenFor(user);
        await SearchAsync(token, "rozgrzewka"); // warm the pipeline (JIT, connection, codegen)

        var sw = Stopwatch.StartNew();
        var result = await SearchAsync(token, "mleko");
        sw.Stop();

        result.Tasks.Select(t => t.Id).Should().Equal(needle);
        Console.WriteLine($"[SC-009] search over 10 001 tasks took {sw.ElapsedMilliseconds} ms end-to-end");
        sw.ElapsedMilliseconds.Should().BeLessThan(250, "SC-009 target 50 ms server-side; 250 ms is the CI ceiling incl. HTTP + Testcontainers");
    }
}
