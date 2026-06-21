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
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? CompletedAt);

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
