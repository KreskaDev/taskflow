using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.IntegrationTests.Infrastructure;
using TaskFlow.Infrastructure.Persistence;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;
using TaskId = TaskFlow.Domain.TaskManagement.TaskId;

namespace TaskFlow.IntegrationTests.Labels;

/// <summary>
/// Allow + the SC-016 deny matrix for <c>PATCH /api/tasks/{id}/labels</c> (operationId <c>setTaskLabels</c>,
/// slice 006). Two-sided authz (task write-access AND caller-owned labels), per-user isolation, idempotent,
/// VERSIONLESS. Deny matrix: viewer → 403, non-member → 404, personal-foreign → 404, non-owned label → 422.
/// </summary>
public sealed class SetTaskLabelsTests : SharingTestBase
{
    private static string LabelsPath(Guid id) => $"/api/tasks/{id}/labels";

    private async Task<Guid> CreateLabelAsync(string token, string name)
    {
        var id = Guid.CreateVersion7();
        using var response = await SendAsync(HttpMethod.Put, $"/api/labels/{id}", token, new { name });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return id;
    }

    [Fact]
    public async Task Allow_apply_and_remove_own_labels_on_a_personal_task()
    {
        var user = await CreateUserAsync("g-stl-p", "stlp@example.com", "Owner");
        var token = TokenFor(user);
        var l1 = await CreateLabelAsync(token, "Urgent");
        var l2 = await CreateLabelAsync(token, "Home");
        var taskId = await SeedTaskAsync(user, "Personal task");

        using (var apply = await SendAsync(HttpMethod.Patch, LabelsPath(taskId), token, new { labelIds = new[] { l1, l2 } }))
        {
            apply.StatusCode.Should().Be(HttpStatusCode.OK);
            (await apply.ReadTaskAsync()).Labels.Should().BeEquivalentTo([l1, l2]);
        }

        using var remove = await SendAsync(HttpMethod.Patch, LabelsPath(taskId), token, new { labelIds = new[] { l1 } });
        (await remove.ReadTaskAsync()).Labels.Should().BeEquivalentTo([l1], "the whole-set replace removed l2");
    }

    [Fact]
    public async Task Allow_an_editor_applies_own_labels_on_a_shared_task()
    {
        var owner = await CreateUserAsync("g-stl-so", "stlso@example.com", "Owner");
        var editor = await CreateUserAsync("g-stl-sed", "stlsed@example.com", "Editor");
        var ownerToken = TokenFor(owner);
        var project = await ShareProjectAsync(ownerToken, await CreateProjectAsync(ownerToken));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        var taskId = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);
        var editorLabel = await CreateLabelAsync(TokenFor(editor), "EditorTag");

        using var response = await SendAsync(HttpMethod.Patch, LabelsPath(taskId), TokenFor(editor), new { labelIds = new[] { editorLabel } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadTaskAsync()).Labels.Should().BeEquivalentTo([editorLabel]);
    }

    [Fact]
    public async Task Per_user_isolation_a_set_replace_does_not_touch_another_members_labels()
    {
        var owner = await CreateUserAsync("g-stl-iso-o", "stlisoo@example.com", "Owner");
        var editor = await CreateUserAsync("g-stl-iso-e", "stlisoe@example.com", "Editor");
        var ownerToken = TokenFor(owner);
        var project = await ShareProjectAsync(ownerToken, await CreateProjectAsync(ownerToken));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        var taskId = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);

        var ownerLabel = await CreateLabelAsync(ownerToken, "OwnerTag");
        var editorLabel = await CreateLabelAsync(TokenFor(editor), "EditorTag");
        await SendAsync(HttpMethod.Patch, LabelsPath(taskId), ownerToken, new { labelIds = new[] { ownerLabel } });

        using var response = await SendAsync(HttpMethod.Patch, LabelsPath(taskId), TokenFor(editor), new { labelIds = new[] { editorLabel } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadTaskAsync()).Labels.Should().BeEquivalentTo([editorLabel], "the editor sees ONLY their own labels (caller-scoped)");

        // The owner's row on the same shared task is untouched: both applications persist.
        var rows = await LoadTaskLabelIdsAsync(taskId);
        rows.Should().BeEquivalentTo([ownerLabel, editorLabel], "the per-user replace touched only the editor's rows");
    }

    [Fact]
    public async Task Idempotent_no_op_set()
    {
        var user = await CreateUserAsync("g-stl-idem", "stlidem@example.com", "Owner");
        var token = TokenFor(user);
        var l1 = await CreateLabelAsync(token, "Urgent");
        var taskId = await SeedTaskAsync(user, "Personal task");
        await SendAsync(HttpMethod.Patch, LabelsPath(taskId), token, new { labelIds = new[] { l1 } });

        using var response = await SendAsync(HttpMethod.Patch, LabelsPath(taskId), token, new { labelIds = new[] { l1 } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadTaskAsync()).Labels.Should().BeEquivalentTo([l1]);
        (await LoadTaskLabelIdsAsync(taskId)).Should().BeEquivalentTo([l1], "a no-op set leaves exactly one row");
    }

    [Fact]
    public async Task Versionless_setting_labels_does_not_bump_task_version()
    {
        var user = await CreateUserAsync("g-stl-ver", "stlver@example.com", "Owner");
        var token = TokenFor(user);
        var l1 = await CreateLabelAsync(token, "Urgent");
        var taskId = await SeedTaskAsync(user, "Personal task");
        var versionBefore = (await LoadTaskAsync(taskId))!.Version;

        await SendAsync(HttpMethod.Patch, LabelsPath(taskId), token, new { labelIds = new[] { l1 } });

        (await LoadTaskAsync(taskId))!.Version.Should().Be(versionBefore, "a per-user label toggle never bumps the shared Task.version (R2)");
    }

    [Fact]
    public async Task Deny_a_viewer_is_403()
    {
        var owner = await CreateUserAsync("g-stl-vo", "stlvo@example.com", "Owner");
        var viewer = await CreateUserAsync("g-stl-vv", "stlvv@example.com", "Viewer");
        var ownerToken = TokenFor(owner);
        var project = await ShareProjectAsync(ownerToken, await CreateProjectAsync(ownerToken));
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);
        var taskId = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);
        var viewerLabel = await CreateLabelAsync(TokenFor(viewer), "ViewerTag");

        using var response = await SendAsync(HttpMethod.Patch, LabelsPath(taskId), TokenFor(viewer), new { labelIds = new[] { viewerLabel } });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a viewer is read-only and cannot tag a shared task (FR-067)");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("forbidden");
    }

    [Fact]
    public async Task Deny_a_non_member_is_404()
    {
        var owner = await CreateUserAsync("g-stl-no", "stlno@example.com", "Owner");
        var stranger = await CreateUserAsync("g-stl-nx", "stlnx@example.com", "Stranger");
        var ownerToken = TokenFor(owner);
        var project = await ShareProjectAsync(ownerToken, await CreateProjectAsync(ownerToken));
        var taskId = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);
        var strangerLabel = await CreateLabelAsync(TokenFor(stranger), "StrangerTag");

        using var response = await SendAsync(HttpMethod.Patch, LabelsPath(taskId), TokenFor(stranger), new { labelIds = new[] { strangerLabel } });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a non-member cannot observe the shared task");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_a_personal_task_not_owned_by_the_caller_is_404()
    {
        var owner = await CreateUserAsync("g-stl-po", "stlpo@example.com", "Owner");
        var other = await CreateUserAsync("g-stl-px", "stlpx@example.com", "Other");
        var taskId = await SeedTaskAsync(owner, "Owner's personal task");
        var otherLabel = await CreateLabelAsync(TokenFor(other), "OtherTag");

        using var response = await SendAsync(HttpMethod.Patch, LabelsPath(taskId), TokenFor(other), new { labelIds = new[] { otherLabel } });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "a personal task is invisible to non-owners");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("not_found");
    }

    [Fact]
    public async Task Deny_a_non_owned_label_is_422_and_changes_nothing()
    {
        var user = await CreateUserAsync("g-stl-lbl", "stllbl@example.com", "Owner");
        var other = await CreateUserAsync("g-stl-lbl2", "stllbl2@example.com", "Other");
        var token = TokenFor(user);
        var taskId = await SeedTaskAsync(user, "Personal task");
        var foreignLabel = await CreateLabelAsync(TokenFor(other), "ForeignTag");

        using var response = await SendAsync(HttpMethod.Patch, LabelsPath(taskId), token, new { labelIds = new[] { foreignLabel } });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "every label must be one the caller owns");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
        (await LoadTaskLabelIdsAsync(taskId)).Should().BeEmpty("the rejected set changed nothing");
    }

    [Fact]
    public async Task Deny_a_non_owned_label_on_a_shared_task_is_422()
    {
        // The label-ownership side of the AND must hold even when the TASK gate passes (a shared-project editor).
        var owner = await CreateUserAsync("g-stl-sh-o", "stlsho@example.com", "Owner");
        var editor = await CreateUserAsync("g-stl-sh-e", "stlshe@example.com", "Editor");
        var ownerToken = TokenFor(owner);
        var project = await ShareProjectAsync(ownerToken, await CreateProjectAsync(ownerToken));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        var taskId = await SeedTaskAsync(owner, "Shared task", projectId: project.Id);
        var ownerLabel = await CreateLabelAsync(ownerToken, "OwnerTag"); // a co-member's label, not the editor's

        using var response = await SendAsync(HttpMethod.Patch, LabelsPath(taskId), TokenFor(editor), new { labelIds = new[] { ownerLabel } });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "label-ownership is enforced even when the task gate passes");
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("validation_failed");
        (await LoadTaskLabelIdsAsync(taskId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Deny_no_valid_session_is_401_and_writes_no_row()
    {
        var user = await CreateUserAsync("g-stl-401", "stl401@example.com", "Owner");
        var taskId = await SeedTaskAsync(user, "Personal task");
        var label = await CreateLabelAsync(TokenFor(user), "Urgent");

        using var response = await SendAsync(HttpMethod.Patch, LabelsPath(taskId), TestJwtHelper.WrongKey("nobody"), new { labelIds = new[] { label } });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync()).ErrorCode.Should().Be("unauthenticated");
        (await LoadTaskLabelIdsAsync(taskId)).Should().BeEmpty("deny-by-default must weave on the label-on-task route — no row written");
    }

    private async Task<IReadOnlyList<Guid>> LoadTaskLabelIdsAsync(Guid taskId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.TaskLabels
            .Where(tl => tl.TaskId == TaskId.From(taskId))
            .Select(tl => tl.LabelId.Value)
            .ToListAsync();
    }
}
