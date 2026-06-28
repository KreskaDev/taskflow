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

/// <summary>The <c>UserProfile</c> response contract (contracts/openapi.yaml).</summary>
public sealed record ProfileBody(Guid Id, string Email, string DisplayName, string? AvatarUrl, DateTime CreatedAt);

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
    Guid? ProjectId = null, string? Priority = null, string? Description = null);

/// <summary>A Today row (slice 005): the lean TaskResponse fields PLUS the Today-only <c>isOverdue</c> flag.</summary>
public sealed record TodayTaskBody(
    Guid Id, string Title, string Status, string Position, int Version,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? CompletedAt,
    DateTime? DueDate, bool? DueHasTime, Guid? ProjectId, string? Priority, string? Description,
    bool IsOverdue);

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

    public static async Task<ProblemBody> ReadProblemAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return (await response.Content.ReadFromJsonAsync<ProblemBody>(Web))
            ?? throw new InvalidOperationException("Expected a ProblemDetails body but the response was empty.");
    }

    /// <summary>The media type of the response body (e.g. <c>application/problem+json</c>), independent of charset.</summary>
    public static string? MediaType(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Content.Headers.ContentType?.MediaType;
    }
}
