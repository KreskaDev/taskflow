using System.Globalization;
using System.Net;
using FluentAssertions;
using FluentAssertions.Execution;
using TaskFlow.Domain.IdentityAccess;
using TaskFlow.IntegrationTests.Infrastructure;
using MembershipRoles = TaskFlow.Domain.TaskManagement.MembershipRoles;

namespace TaskFlow.IntegrationTests.TaskManagement;

/// <summary>
/// THE SC-016 role × operation deny matrix (T033), asserted through the real handlers. Per the
/// governance gate (Constitution VIII/IX) and self-review M2, this covers EVERY changed manage handler —
/// the membership ops (invite/change-role/remove/unshare/transfer) AND the slice-004 manage commands the
/// visibility dispatch now touches (delete/edit/archive/unarchive). Rows: editor/viewer-denied-manage
/// (403), non-member-denied-read (404), removed-member-loses-access (404), last-owner-guard (409). (The
/// viewer-denied-WRITE row is asserted at the policy contract in the unit tests — the task-write handler
/// that consumes it on shared projects arrives slice 008; data-model §3.)
/// </summary>
public sealed class RoleOperationDenyMatrixTests : SharingTestBase
{
    private sealed record Fixture(ProjectBody Project, UserId Owner, UserId Editor, UserId Viewer, UserId Stranger, UserId Target);

    private async Task<Fixture> SetupAsync(string slug)
    {
        var owner = await CreateUserAsync($"g-{slug}-o", $"{slug}o@example.com", "Owner A");
        var editor = await CreateUserAsync($"g-{slug}-ed", $"{slug}ed@example.com", "Editor B");
        var viewer = await CreateUserAsync($"g-{slug}-vw", $"{slug}vw@example.com", "Viewer C");
        var stranger = await CreateUserAsync($"g-{slug}-x", $"{slug}x@example.com", "Stranger X");
        var target = await CreateUserAsync($"g-{slug}-t", $"{slug}t@example.com", "Target T");
        var token = TokenFor(owner);
        var project = await ShareProjectAsync(token, await CreateProjectAsync(token));
        await SeedMembershipAsync(project.Id, editor, MembershipRoles.Editor);
        await SeedMembershipAsync(project.Id, viewer, MembershipRoles.Viewer);
        return new Fixture(project, owner, editor, viewer, stranger, target);
    }

    /// <summary>Issues each of the nine manage ops as <paramref name="callerToken"/>; returns (opName, status) pairs.</summary>
    private async Task<List<(string Op, HttpStatusCode Status)>> SweepManageOpsAsync(Fixture f, string callerToken)
    {
        var p = f.Project;
        var v = p.Version;
        var vq = v.ToString(CultureInfo.InvariantCulture);
        var results = new List<(string, HttpStatusCode)>();

        async Task Run(string op, HttpMethod method, string path, object? body)
        {
            using var r = await SendAsync(method, path, callerToken, body);
            results.Add((op, r.StatusCode));
        }

        await Run("invite", HttpMethod.Post, $"/api/projects/{p.Id}/members", new { email = "unknown-target@example.com", role = MembershipRoles.Viewer, version = v });
        await Run("change-role", HttpMethod.Patch, $"/api/projects/{p.Id}/members/{f.Viewer.Value}", new { role = MembershipRoles.Editor, version = v });
        await Run("remove", HttpMethod.Delete, $"/api/projects/{p.Id}/members/{f.Viewer.Value}?version={vq}", null);
        await Run("unshare", HttpMethod.Patch, $"/api/projects/{p.Id}/unshare", new { version = v });
        await Run("transfer", HttpMethod.Patch, $"/api/projects/{p.Id}/owner", new { userId = f.Viewer.Value, version = v });
        await Run("delete", HttpMethod.Delete, $"/api/projects/{p.Id}?version={vq}", null);
        await Run("edit", HttpMethod.Patch, $"/api/projects/{p.Id}", new { name = "Hijacked", color = "blue", icon = "folder", parentId = (Guid?)null, version = v });
        await Run("archive", HttpMethod.Patch, $"/api/projects/{p.Id}/archive", new { version = v });
        await Run("unarchive", HttpMethod.Patch, $"/api/projects/{p.Id}/unarchive", new { version = v });

        return results;
    }

    [Fact]
    public async Task Editor_is_denied_every_manage_op_403()
    {
        var f = await SetupAsync("dm-ed");

        var results = await SweepManageOpsAsync(f, TokenFor(f.Editor));

        using (new AssertionScope())
        {
            foreach (var (op, status) in results)
            {
                status.Should().Be(HttpStatusCode.Forbidden, $"an editor lacks the owner role for '{op}' (R9)");
            }
        }
    }

    [Fact]
    public async Task Viewer_is_denied_every_manage_op_403()
    {
        var f = await SetupAsync("dm-vw");

        var results = await SweepManageOpsAsync(f, TokenFor(f.Viewer));

        using (new AssertionScope())
        {
            foreach (var (op, status) in results)
            {
                status.Should().Be(HttpStatusCode.Forbidden, $"a viewer lacks the owner role for '{op}' (R9)");
            }
        }
    }

    [Fact]
    public async Task Non_member_is_denied_every_read_404()
    {
        var f = await SetupAsync("dm-nm");
        await SeedTaskUnderProjectAsync(f.Owner, f.Project.Id, "Owner's task", "a0");
        var x = TokenFor(f.Stranger);

        using var tasks = await SendAsync(HttpMethod.Get, $"/api/projects/{f.Project.Id}/tasks", x);
        using var roster = await SendAsync(HttpMethod.Get, $"/api/projects/{f.Project.Id}/members", x);

        using (new AssertionScope())
        {
            tasks.StatusCode.Should().Be(HttpStatusCode.NotFound, "a non-member cannot read the tasks (R9)");
            roster.StatusCode.Should().Be(HttpStatusCode.NotFound, "a non-member cannot read the roster (R9)");
        }
    }

    [Fact]
    public async Task Removed_member_loses_all_access_404()
    {
        var f = await SetupAsync("dm-rm");
        await SeedTaskUnderProjectAsync(f.Owner, f.Project.Id, "Owner's task", "a0");

        using (var remove = await SendAsync(HttpMethod.Delete, $"/api/projects/{f.Project.Id}/members/{f.Editor.Value}?version={f.Project.Version.ToString(CultureInfo.InvariantCulture)}", TokenFor(f.Owner)))
        {
            remove.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var removed = TokenFor(f.Editor);
        using var tasks = await SendAsync(HttpMethod.Get, $"/api/projects/{f.Project.Id}/tasks", removed);
        using var roster = await SendAsync(HttpMethod.Get, $"/api/projects/{f.Project.Id}/members", removed);

        using (new AssertionScope())
        {
            tasks.StatusCode.Should().Be(HttpStatusCode.NotFound, "a removed member is a non-member → 404 (R10)");
            roster.StatusCode.Should().Be(HttpStatusCode.NotFound, "a removed member is a non-member → 404 (R10)");
        }
    }

    [Fact]
    public async Task Last_owner_guard_409_on_leave_remove_and_demote()
    {
        var f = await SetupAsync("dm-lo");
        var ownerToken = TokenFor(f.Owner);
        var p = f.Project;
        var vq = p.Version.ToString(CultureInfo.InvariantCulture);

        using var leave = await SendAsync(HttpMethod.Delete, $"/api/projects/{p.Id}/membership?version={vq}", ownerToken);
        using var remove = await SendAsync(HttpMethod.Delete, $"/api/projects/{p.Id}/members/{f.Owner.Value}?version={vq}", ownerToken);
        using var demote = await SendAsync(HttpMethod.Patch, $"/api/projects/{p.Id}/members/{f.Owner.Value}", ownerToken, new { role = MembershipRoles.Viewer, version = p.Version });

        using (new AssertionScope())
        {
            leave.StatusCode.Should().Be(HttpStatusCode.Conflict, "the owner cannot leave (R7)");
            remove.StatusCode.Should().Be(HttpStatusCode.Conflict, "the owner cannot be removed (R7)");
            demote.StatusCode.Should().Be(HttpStatusCode.Conflict, "the owner cannot be demoted (R7)");
            (await leave.ReadProblemAsync()).ErrorCode.Should().Be("last_owner");
            (await remove.ReadProblemAsync()).ErrorCode.Should().Be("last_owner");
            (await demote.ReadProblemAsync()).ErrorCode.Should().Be("last_owner");
        }
    }
}
