using System.Linq;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace TaskFlow.Api.OpenApi;

/// <summary>
/// Enriches the generated OpenAPI document so the published <c>/openapi/v1.json</c> matches the
/// curated reference contract (Constitution VI; ADR-0009). The .NET built-in generator does not, on
/// its own, surface the RFC 9457 <c>ProblemDetails</c> envelope, stable operationIds, or the
/// documented <c>401</c> responses — yet the generated TypeScript client (<c>apps/web/.../client.ts</c>)
/// imports <c>components["schemas"]["ProblemDetails"]</c>. Without this transformer, regenerating the
/// client from the live document drops that type and breaks the web typecheck. This restores:
/// <list type="bullet">
///   <item>the <c>ProblemDetails</c> component schema (with the stable <c>errorCode</c> enum),</item>
///   <item>stable operationIds (<c>ensureUser</c> / <c>getCurrentUser</c> / <c>deleteAccount</c>),</item>
///   <item>a documented <c>401</c> error response referencing <c>ProblemDetails</c>.</item>
/// </list>
/// </summary>
internal sealed class TaskFlowDocumentTransformer : IOpenApiDocumentTransformer
{
    private const string ProblemDetailsSchemaId = "ProblemDetails";

    /// <summary>The stable error-code enum from contracts/openapi.yaml (mirrors ProblemDetailsMiddleware).</summary>
    private static readonly string[] ErrorCodes =
    [
        "validation_failed", "unauthenticated", "not_admitted", "forbidden",
        "not_found", "conflict_lww", "last_owner", "internal_error",
        "version_conflict",
    ];

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Components ??= new OpenApiComponents();
        document.Components.Schemas[ProblemDetailsSchemaId] = BuildProblemDetailsSchema();

        // Stable operationIds + a documented 401 on the authenticated user endpoints (deny-by-default).
        SetOperation(document, "/api/users/ensure", OperationType.Post, "ensureUser");
        SetOperation(document, "/api/users/me", OperationType.Get, "getCurrentUser");
        SetOperation(document, "/api/users/me", OperationType.Delete, "deleteAccount");

        return Task.CompletedTask;
    }

    private static void SetOperation(OpenApiDocument document, string path, OperationType method, string operationId)
    {
        if (document.Paths.TryGetValue(path, out var item) &&
            item.Operations.TryGetValue(method, out var operation))
        {
            operation.OperationId = operationId;
            operation.Responses["401"] = new OpenApiResponse
            {
                Description = "Missing or invalid identity carrier (deny-by-default).",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/problem+json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.Schema,
                                Id = ProblemDetailsSchemaId,
                            },
                        },
                    },
                },
            };
        }
    }

    private static OpenApiSchema BuildProblemDetailsSchema() => new()
    {
        Type = "object",
        Description = "RFC 9457 ProblemDetails — the canonical error envelope for all non-2xx responses (ADR-0009).",
        Properties = new Dictionary<string, OpenApiSchema>
        {
            ["type"] = new() { Type = "string", Format = "uri", Description = "Error type URI." },
            ["title"] = new() { Type = "string", Description = "Human-readable summary." },
            ["status"] = new() { Type = "integer", Format = "int32", Description = "HTTP status code." },
            ["detail"] = new() { Type = "string", Nullable = true, Description = "Human-readable explanation." },
            ["instance"] = new() { Type = "string", Description = "URI identifying the specific occurrence." },
            ["errorCode"] = new()
            {
                Type = "string",
                Description = "Stable machine-readable error code.",
                Enum = ErrorCodes.Select(code => (IOpenApiAny)new OpenApiString(code)).ToList(),
            },
            ["errors"] = new()
            {
                Type = "object",
                Nullable = true,
                Description = "Field-level validation errors (field path -> messages).",
                AdditionalProperties = new OpenApiSchema
                {
                    Type = "array",
                    Items = new OpenApiSchema { Type = "string" },
                },
            },
        },
        Required = new HashSet<string> { "type", "title", "status", "errorCode" },
    };
}
