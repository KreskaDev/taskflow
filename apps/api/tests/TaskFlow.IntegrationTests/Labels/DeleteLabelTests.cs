using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.IntegrationTests.Infrastructure;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.IntegrationTests.Labels;

/// <summary>
/// Allow + deny coverage for <c>DELETE /api/labels/{id}</c> (operationId <c>deleteLabel</c>, slice 006).
/// Hard delete; the <c>task_labels.label_id</c> FK cascade removes the label's applications; not-owned → 404.
/// </summary>
public sealed class DeleteLabelTests : SharingTestBase
{
    private static string LabelPath(Guid id) => $"/api/labels/{id}";

    [Fact]
    public async Task Allow_delete_removes_the_label_and_cascades_its_applications()
    {
        var user = await CreateUserAsync("g-dl-o", "dlo@example.com", "Owner");
        var token = TokenFor(user);
        var labelId = Guid.CreateVersion7();
        await SendAsync(HttpMethod.Put, LabelPath(labelId), token, new { name = "Urgent" });
        var taskId = await SeedTaskAsync(user, "Task with a label");
        using (var apply = await SendAsync(HttpMethod.Patch, $"/api/tasks/{taskId}/labels", token, new { labelIds = new[] { labelId } }))
        {
            apply.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var response = await SendAsync(HttpMethod.Delete, LabelPath(labelId), token);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await AssertNoLabelAsync(labelId);
        (await CountTaskLabelsAsync()).Should().Be(0, "the FK cascade removes the label's task applications");
    }

    [Fact]
    public async Task Deny_a_label_not_owned_is_404()
    {
        var alice = await CreateUserAsync("g-dl-a", "dla@example.com", "Alice");
        var bob = await CreateUserAsync("g-dl-b", "dlb@example.com", "Bob");
        var labelId = Guid.CreateVersion7();
        await SendAsync(HttpMethod.Put, LabelPath(labelId), TokenFor(alice), new { name = "AliceLabel" });

        using var response = await SendAsync(HttpMethod.Delete, LabelPath(labelId), TokenFor(bob));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_an_absent_label_is_404()
    {
        var user = await CreateUserAsync("g-dl-abs", "dlabs@example.com", "Owner");

        using var response = await SendAsync(HttpMethod.Delete, LabelPath(Guid.CreateVersion7()), TokenFor(user));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task AssertNoLabelAsync(Guid labelId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Labels.AnyAsync(l => l.Id == TaskFlow.Domain.TaskManagement.LabelId.From(labelId)))
            .Should().BeFalse("the label row is hard-deleted");
    }

    private async Task<int> CountTaskLabelsAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.TaskLabels.CountAsync();
    }
}
