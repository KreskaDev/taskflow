using System.Net.Http.Json;
using System.Text.Json;

namespace TaskFlow.IntegrationTests.Infrastructure;

/// <summary>The decoded RFC 9457 ProblemDetails envelope (ADR-0009) returned on any non-2xx response.</summary>
/// <remarks>
/// <see cref="Errors"/> is the optional field-level validation map (field path -> messages) the host
/// emits on a <c>validation_failed</c> (422); absent on errors that carry no field detail, hence nullable.
/// </remarks>
public sealed record ProblemBody(
    string? Type, string? Title, int Status, string? ErrorCode, string? Instance,
    IReadOnlyDictionary<string, string[]>? Errors = null);

/// <summary>The <c>UserProfile</c> response contract (contracts/openapi.yaml). <c>CycleDefaultDurationDays</c>
/// is the slice-011 D8 preference (default 14), optional so pre-011 fixtures stay valid.</summary>
public sealed record ProfileBody(
    Guid Id, string Email, string DisplayName, string? AvatarUrl, DateTime CreatedAt,
    int? CycleDefaultDurationDays = null);

/// <summary>
/// The lean <c>TaskResponse</c> read model (contracts/openapi.yaml). Only the members the
/// list tests assert on are declared; the JSON serializer ignores the rest of the payload.
/// </summary>
public sealed record TaskListItem(Guid Id, string Title, string Status, string Position, int Version);

/// <summary>
/// The FULL lean <c>TaskResponse</c> read model (contracts/openapi.yaml), including the timestamp
/// members the single-item create round-trip asserts on. Declared in the test assembly so the RED
/// createTask spec decodes the wire body WITHOUT referencing the production <c>TaskResponse</c> DTO
/// (it lands in T031) — the file compiles and fails at runtime (the cleaner RED).
/// </summary>
public sealed record TaskBody(
    Guid Id, string Title, string Status, string Position, int Version,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? CompletedAt,
    DateTime? DueDate = null, bool? DueHasTime = null,
    Guid? ProjectId = null, string? Priority = null, string? Description = null,
    IReadOnlyList<Guid>? Assignees = null, IReadOnlyList<Guid>? Labels = null,
    Guid? CycleId = null, bool? CarriedOver = null);

/// <summary>The <c>LabelResponse</c> read model (slice 006): id + name + optional preset color. No ownerId.</summary>
public sealed record LabelBody(Guid Id, string Name, string? Color = null);

/// <summary>The <c>ViewCountsResponse</c> read model (slice 019, FR-109): per-view incomplete counts.</summary>
public sealed record ProjectCountBody(Guid ProjectId, int Count);

/// <summary>The <c>ViewCountsResponse</c> envelope (slice 019, contracts/view-counts.md).</summary>
public sealed record CountsBody(int Inbox, int Today, int Upcoming, int Assigned, IReadOnlyList<ProjectCountBody> Projects);

/// <summary>An "Assigned to me" group (slice 008): a shared project and the caller's assigned tasks in it.</summary>
public sealed record AssignedGroupBody(Guid ProjectId, IReadOnlyList<TaskBody> Tasks);

/// <summary>The <c>AssignedResponse</c> envelope (slice 008): the caller's assigned tasks grouped by project.</summary>
public sealed record AssignedBody(IReadOnlyList<AssignedGroupBody> Groups);

/// <summary>A Today row (slice 005): the lean TaskResponse fields PLUS the Today-only <c>isOverdue</c> flag.</summary>
public sealed record TodayTaskBody(
    Guid Id, string Title, string Status, string Position, int Version,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? CompletedAt,
    DateTime? DueDate, bool? DueHasTime, Guid? ProjectId, string? Priority, string? Description,
    bool IsOverdue, IReadOnlyList<Guid>? Labels = null,
    Guid? CycleId = null, bool? CarriedOver = null);

/// <summary>A Today group (slice 005): the owning project (null = Inbox) and its ordered rows.</summary>
public sealed record TodayGroupBody(Guid? ProjectId, IReadOnlyList<TodayTaskBody> Tasks);

/// <summary>The <c>TodayResponse</c> envelope (slice 005): tasks grouped by project.</summary>
public sealed record TodayBody(IReadOnlyList<TodayGroupBody> Groups);

/// <summary>An Upcoming group (slice 005): a Warsaw calendar day and its ordered rows.</summary>
public sealed record UpcomingGroupBody(string Date, IReadOnlyList<TaskBody> Tasks);

/// <summary>The <c>UpcomingResponse</c> envelope (slice 005): tasks grouped by Warsaw day.</summary>
public sealed record UpcomingBody(IReadOnlyList<UpcomingGroupBody> Groups);

/// <summary>
/// The lean <c>ProjectResponse</c> read model (contracts/openapi.yaml, slice 004). Declared in the
/// test assembly so the project RED specs decode the wire body WITHOUT referencing the production
/// <c>ProjectResponse</c> DTO — the files compile and fail at runtime (the cleaner RED). NEVER carries
/// <c>ownerId</c>/<c>deletedAt</c> (the read-model leak rule, data-model §4).
/// </summary>
public sealed record ProjectBody(
    Guid Id, string Name, string Color, string Icon, Guid? ParentId, string Visibility,
    DateTime? ArchivedAt, int Version, DateTime CreatedAt, DateTime UpdatedAt, string? Role = null);

/// <summary>A single <c>MemberResponse</c> roster entry (slice 007). NEVER carries an email (Constitution XI).</summary>
public sealed record MemberBody(Guid UserId, string DisplayName, string Role, bool IsOwner);

/// <summary>The <c>MembersResponse</c> roster body (slice 007): the composed roster + the project <c>version</c>.</summary>
public sealed record MembersBody(Guid ProjectId, int Version, IReadOnlyList<MemberBody> Members);

/// <summary>A resolved @mention token in a <c>CommentResponse</c> (slice 009); <c>userId</c> null = erased tombstone (R11).</summary>
public sealed record CommentMentionBody(Guid? UserId, string DisplayName);

/// <summary>
/// The <c>CommentResponse</c> read model (slice 009, contracts/openapi.yaml). <c>authorId</c> null =
/// tombstoned author (renders "Deleted user"); <c>canEdit</c> is the caller-is-author UI convenience.
/// NEVER carries an email (Constitution XI).
/// </summary>
public sealed record CommentBody(
    Guid Id, Guid TaskId, Guid? AuthorId, string AuthorDisplayName, string Body,
    IReadOnlyList<CommentMentionBody> Mentions, DateTime CreatedAt, DateTime? EditedAt, bool CanEdit);

/// <summary>The <c>CommentListResponse</c> envelope (slice 009): a task's chronological live thread.</summary>
public sealed record CommentListBody(Guid TaskId, IReadOnlyList<CommentBody> Comments);

/// <summary>
/// The per-status breakdown of a cycle's metrics (slice 011, D6). The wire key of
/// <see cref="InProgress"/> is the task-status token <c>in_progress</c> (contracts/cycles-api.md).
/// </summary>
public sealed record CycleBreakdownBody(
    int Backlog, int Todo,
    [property: System.Text.Json.Serialization.JsonPropertyName("in_progress")] int InProgress,
    int Done, int Cancelled);

/// <summary>Computed team-wide metrics of a cycle (slice 011, D6): totals over non-deleted tasks.</summary>
public sealed record CycleMetricsBody(int Total, int Done, CycleBreakdownBody Breakdown);

/// <summary>
/// The <c>CycleResponse</c> read model (slice 011, contracts/cycles-api.md). Declared in the test
/// assembly so the RED specs decode the wire body WITHOUT referencing the production DTO (the
/// slice-004 convention). <c>CreatedAt</c> is exposed for the D5 tiebreaker.
/// </summary>
public sealed record CycleBody(
    Guid Id, string Name, DateTime StartDate, DateTime EndDate, string Status, int Version,
    DateTime CreatedAt, CycleMetricsBody Metrics);

/// <summary>The <c>CloseCycleResponse</c> envelope (slice 011, D4): the closed cycle + rollover counts.</summary>
public sealed record CloseCycleBody(CycleBody Cycle, int RolledToNext, int RolledToBacklog, int Kept);

/// <summary>
/// Helpers for the allow/deny integration tests: read the typed bodies the API emits using the same
/// camelCase (<see cref="JsonSerializerDefaults.Web"/>) conventions the host serializes with.
/// </summary>
public static class ApiResponse
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static async Task<ProfileBody> ReadProfileAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<ProfileBody>(Web))
            ?? throw new InvalidOperationException("Expected a UserProfile body but the response was empty.");
    }

    public static async Task<IReadOnlyList<TaskListItem>> ReadTasksAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<TaskListItem>>(Web))
            ?? throw new InvalidOperationException("Expected a TaskResponse array but the response was empty.");
    }

    public static async Task<TaskBody> ReadTaskAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<TaskBody>(Web))
            ?? throw new InvalidOperationException("Expected a TaskResponse body but the response was empty.");
    }

    public static async Task<ProjectBody> ReadProjectAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<ProjectBody>(Web))
            ?? throw new InvalidOperationException("Expected a ProjectResponse body but the response was empty.");
    }

    public static async Task<IReadOnlyList<ProjectBody>> ReadProjectsAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<ProjectBody>>(Web))
            ?? throw new InvalidOperationException("Expected a ProjectResponse array but the response was empty.");
    }

    public static async Task<MembersBody> ReadMembersAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<MembersBody>(Web))
            ?? throw new InvalidOperationException("Expected a MembersResponse body but the response was empty.");
    }

    public static async Task<MemberBody> ReadMemberAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<MemberBody>(Web))
            ?? throw new InvalidOperationException("Expected a MemberResponse body but the response was empty.");
    }

    public static async Task<TodayBody> ReadTodayAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<TodayBody>(Web))
            ?? throw new InvalidOperationException("Expected a TodayResponse body but the response was empty.");
    }

    public static async Task<UpcomingBody> ReadUpcomingAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<UpcomingBody>(Web))
            ?? throw new InvalidOperationException("Expected an UpcomingResponse body but the response was empty.");
    }

    public static async Task<IReadOnlyList<TaskBody>> ReadTaskBodiesAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<TaskBody>>(Web))
            ?? throw new InvalidOperationException("Expected a TaskResponse array but the response was empty.");
    }

    public static async Task<AssignedBody> ReadAssignedAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<AssignedBody>(Web))
            ?? throw new InvalidOperationException("Expected an AssignedResponse body but the response was empty.");
    }

    public static async Task<LabelBody> ReadLabelAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<LabelBody>(Web))
            ?? throw new InvalidOperationException("Expected a LabelResponse body but the response was empty.");
    }

    public static async Task<IReadOnlyList<LabelBody>> ReadLabelsAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<LabelBody>>(Web))
            ?? throw new InvalidOperationException("Expected a LabelResponse array but the response was empty.");
    }

    public static async Task<CommentBody> ReadCommentAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<CommentBody>(Web))
            ?? throw new InvalidOperationException("Expected a CommentResponse body but the response was empty.");
    }

    public static async Task<CommentListBody> ReadCommentListAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<CommentListBody>(Web))
            ?? throw new InvalidOperationException("Expected a CommentListResponse body but the response was empty.");
    }

    public static async Task<CycleBody> ReadCycleAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<CycleBody>(Web))
            ?? throw new InvalidOperationException("Expected a CycleResponse body but the response was empty.");
    }

    public static async Task<IReadOnlyList<CycleBody>> ReadCyclesAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<CycleBody>>(Web))
            ?? throw new InvalidOperationException("Expected a CycleResponse array but the response was empty.");
    }

    public static async Task<CloseCycleBody> ReadCloseCycleAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<CloseCycleBody>(Web))
            ?? throw new InvalidOperationException("Expected a CloseCycleResponse body but the response was empty.");
    }

    public static async Task<ProblemBody> ReadProblemAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<ProblemBody>(Web))
            ?? throw new InvalidOperationException("Expected a ProblemDetails body but the response was empty.");
    }

    public static async Task<CountsBody> ReadCountsAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<CountsBody>(Web))
            ?? throw new InvalidOperationException("Expected a ViewCountsResponse body but the response was empty.");
    }

    /// <summary>The media type of the response body (e.g. <c>application/problem+json</c>), independent of charset.</summary>
    public static string? MediaType(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Content.Headers.ContentType?.MediaType;
    }
}
