using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.IntegrationTests.Infrastructure;
using TaskEntity = TaskFlow.Domain.TaskManagement.Task;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// Constitution XI cascade guarantee (T016): the <c>tasks.created_by → users(id)</c> FK is
/// <c>ON DELETE CASCADE</c>, so deleting an account hard-deletes that user's tasks in the same
/// transaction — no orphaned rows, no FK violation. This is a GREEN regression test: the cascade
/// already exists in the AddTasks migration. Account deletion is exercised via the same path
/// slice-001's <c>DeleteAccountTests</c> uses — the authenticated <c>DELETE /api/users/me</c>
/// endpoint, where the carrier's <c>sub</c> is the caller's own TaskFlow user id.
/// </summary>
public sealed class AccountDeletionCascadeTests : IntegrationTestBase
{
    private const string MePath = "/api/users/me";
    private const string EnsurePath = "/api/users/ensure";

    private async Task<ProfileBody> CreateAccountAsync(string sub, string email)
    {
        return await (await SendAsync(
            HttpMethod.Post, EnsurePath, TestJwtHelper.Valid(sub),
            new { googleSubjectId = sub, email, displayName = "Cascade Owner", avatarUrl = (string?)null }))
            .ReadProfileAsync();
    }

    [Fact]
    public async Task Deleting_an_account_cascade_erases_that_users_tasks()
    {
        // Arrange: admit a user through the same EnsureUser path the slice-001 tests use.
        var owner = await CreateAccountAsync("google-sub-cascade-001", "cascade001@example.com");
        var ownerId = UserId.From(owner.Id);

        // Seed a task owned by that user DIRECTLY (no CreateTask handler exists yet this slice).
        var taskId = TaskId.From(Guid.NewGuid());
        using (var seedScope = Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            seedDb.Tasks.Add(TaskEntity.Create(taskId, ownerId, "Owned by the deleting user", "a0", DateTime.UtcNow));
            await seedDb.SaveChangesAsync();
        }

        // Act: delete the account via the authenticated endpoint (carrier sub = caller's own id).
        using var response = await SendAsync(
            HttpMethod.Delete, MePath, TestJwtHelper.Valid(owner.Id.ToString()));

        // Assert: deletion succeeds — the cascade absorbed the child task, so no FK violation.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var verifyScope = Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await db.Tasks.AnyAsync(t => t.CreatedBy == ownerId))
            .Should().BeFalse("the FK is ON DELETE CASCADE — the deleting user's tasks are erased with the account");
        (await db.Tasks.AnyAsync(t => t.Id == taskId))
            .Should().BeFalse("the specific seeded task row is gone after the cascade");
        (await db.Users.AnyAsync(u => u.Id == ownerId))
            .Should().BeFalse("the account row itself is hard-deleted (Constitution XI / SC-017)");
    }
}
