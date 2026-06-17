using System.Net.Http.Json;
using System.Text.Json;

namespace TaskFlow.IntegrationTests.Infrastructure;

/// <summary>The decoded RFC 9457 ProblemDetails envelope (ADR-0009) returned on any non-2xx response.</summary>
public sealed record ProblemBody(string? Type, string? Title, int Status, string? ErrorCode, string? Instance);

/// <summary>The <c>UserProfile</c> response contract (contracts/openapi.yaml).</summary>
public sealed record ProfileBody(Guid Id, string Email, string DisplayName, string? AvatarUrl, DateTime CreatedAt);

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
