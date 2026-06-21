using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Allow + deny coverage (SC-013/SC-016, T030) for <c>GET /api/tasks</c> (listTasks).
/// The query is ownership-scoped (createdBy = caller), excludes soft-deleted rows, and
/// is ordered by <c>position</c> then <c>id</c> (FR-102; ORDER BY position, id). This is
/// the FAILING RED spec: the GET /api/tasks endpoint and its query handler do NOT exist
/// yet, so every case that expects a 200 list currently fails (the route is missing).
/// Tasks are seeded DIRECTLY through <see cref="AppDbContext"/> because the createTask
/// handler is likewise unbuilt this far into TDD.
/// </summary>
public sealed class GetMyTasksTests : IntegrationTestBase
{
    private const string TasksPath = "/api/tasks";
    private const string EnsurePath = "/api/users/ensure";

    private async Task<UserId> CreateAccountAsync(string sub, string email)
    {
        var profile = await (await SendAsync(
            HttpMethod.Post, EnsurePath, TestJwtHelper.Valid(sub),
            new { googleSubjectId = sub, email, displayName = "List Owner", avatarUrl = (string?)null }))
            .ReadProfileAsync();

        return UserId.From(profile.Id);
    }

    private async Task SeedTaskAsync(UserId owner, string title, string position, bool softDeleted = false)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var task = TaskEntity.Create(TaskId.From(Guid.NewGuid()), owner, title, position, DateTime.UtcNow);
        if (softDeleted)
        {
            task.SoftDelete(DateTime.UtcNow);
        }

        db.Tasks.Add(task);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Allow_returns_only_the_callers_own_tasks_ordered_by_position_then_id()
    {
        var caller = await CreateAccountAsync("google-sub-list-001", "list001@example.com");

        // Seed in an order that does NOT match the position order, so the assertion exercises
        // the server's ORDER BY position, id rather than passing under insertion order.
        await SeedTaskAsync(caller, "Second", "a2");
        await SeedTaskAsync(caller, "First", "a0");
        await SeedTaskAsync(caller, "Middle", "a1");

        using var response = await SendAsync(HttpMethod.Get, TasksPath, TestJwtHelper.Valid(caller.Value.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tasks = await response.ReadTasksAsync();

        tasks.Select(t => t.Title).Should().ContainInOrder("First", "Middle", "Second");
        tasks.Select(t => t.Position).Should().ContainInOrder("a0", "a1", "a2");
        tasks.Should().OnlyContain(t => t.Status == "backlog");
    }

    [Fact]
    public async Task Deny_another_users_tasks_are_absent_from_the_callers_list()
    {
        var caller = await CreateAccountAsync("google-sub-list-002", "list002@example.com");
        var other = await CreateAccountAsync("google-sub-list-003", "list003@example.com");

        await SeedTaskAsync(caller, "Mine", "a0");
        await SeedTaskAsync(other, "Theirs", "a0");

        using var response = await SendAsync(HttpMethod.Get, TasksPath, TestJwtHelper.Valid(caller.Value.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tasks = await response.ReadTasksAsync();

        tasks.Should().ContainSingle(t => t.Title == "Mine");
        tasks.Should().NotContain(t => t.Title == "Theirs");
    }

    [Fact]
    public async Task Deny_a_soft_deleted_task_of_the_caller_is_excluded()
    {
        var caller = await CreateAccountAsync("google-sub-list-004", "list004@example.com");

        await SeedTaskAsync(caller, "Visible", "a0");
        await SeedTaskAsync(caller, "Tombstoned", "a1", softDeleted: true);

        using var response = await SendAsync(HttpMethod.Get, TasksPath, TestJwtHelper.Valid(caller.Value.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tasks = await response.ReadTasksAsync();

        tasks.Should().ContainSingle(t => t.Title == "Visible");
        tasks.Should().NotContain(t => t.Title == "Tombstoned");
    }

    [Fact]
    public async Task Deny_no_jwt_is_rejected_401_with_our_envelope()
    {
        using var response = await Client.GetAsync(new Uri(TasksPath, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.MediaType().Should().Be("application/problem+json");
        var problem = await response.ReadProblemAsync();
        problem.ErrorCode.Should().Be("unauthenticated");
        problem.Status.Should().Be(401);
    }
}
