using System.Globalization;
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

        // Task surface (slice 002, contracts/openapi.yaml). The 200/204 success bodies are auto-emitted
        // from the endpoint signatures; here we restore the friendly operationIds and the exception-driven
        // ProblemDetails responses (401 always, plus the documented 404/409/422 per op) that the .NET
        // generator does not infer. deleteTask carries no 422/409 (version-free, no body); listTasks is
        // the read with only the deny-by-default 401.
        SetOperation(document, "/api/tasks/{id}", OperationType.Put, "createTask", 404, 422);
        SetOperation(document, "/api/tasks", OperationType.Get, "listTasks");
        SetOperation(document, "/api/tasks/{id}/title", OperationType.Patch, "renameTask", 404, 409, 422);
        SetOperation(document, "/api/tasks/{id}/status", OperationType.Patch, "setTaskDone", 404, 409, 422);
        SetOperation(document, "/api/tasks/{id}/position", OperationType.Patch, "reorderTask", 404, 409, 422);
        SetOperation(document, "/api/tasks/{id}", OperationType.Delete, "deleteTask", 404);

        // Project surface (slice 004, contracts/openapi.yaml). Same pattern as the task ops: success
        // bodies auto-emit from the endpoint signatures; here we stamp the stable operationIds and the
        // exception-driven ProblemDetails responses (401 always, plus the documented 404/409/422 per op)
        // the .NET generator does not infer. NO new errorCode — the ErrorCodes enum is unchanged (R12).
        // listProjects is a read with only the deny-by-default 401; createProject has no 409 (idempotent
        // insert, version-free); unarchiveProject has no 422 (version-only body, no preset/nesting field).
        SetOperation(document, "/api/projects/{id}", OperationType.Put, "createProject", 404, 422);
        SetOperation(document, "/api/projects", OperationType.Get, "listProjects");
        SetOperation(document, "/api/projects/{id}", OperationType.Patch, "editProject", 404, 409, 422);
        SetOperation(document, "/api/projects/{id}/archive", OperationType.Patch, "archiveProject", 404, 409, 422);
        SetOperation(document, "/api/projects/{id}/unarchive", OperationType.Patch, "unarchiveProject", 404, 409);
        SetOperation(document, "/api/projects/{id}", OperationType.Delete, "deleteProject", 404, 409, 422);

        // Task ⇆ Project surface (slice 004, US2). moveTaskToProject (the `M` action) checks ownership of
        // BOTH the task and the target project (foreign either → 404), carries the optimistic version
        // (stale → 409), and validates the body (→ 422). listProjectTasks is a read scoped to an owned
        // project (foreign/absent → 404), only the deny-by-default 401 otherwise. No new errorCode (R12).
        SetOperation(document, "/api/tasks/{id}/project", OperationType.Patch, "moveTaskToProject", 404, 409, 422);
        SetOperation(document, "/api/projects/{id}/tasks", OperationType.Get, "listProjectTasks", 404);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stamps a stable <paramref name="operationId"/> on the operation at
    /// <paramref name="path"/>/<paramref name="method"/>, plus the deny-by-default <c>401</c> and any
    /// extra exception-driven ProblemDetails responses (<paramref name="extraProblemStatusCodes"/>, e.g.
    /// <c>404</c>/<c>409</c>/<c>422</c>). All reference the shared <c>ProblemDetails</c> schema. No-ops if
    /// the path is not yet in the document (so the call is safe before its endpoint is mapped).
    /// </summary>
    private static void SetOperation(
        OpenApiDocument document,
        string path,
        OperationType method,
        string operationId,
        params int[] extraProblemStatusCodes)
    {
        if (document.Paths.TryGetValue(path, out var item) &&
            item.Operations.TryGetValue(method, out var operation))
        {
            operation.OperationId = operationId;
            operation.Responses["401"] = ProblemResponse("Missing or invalid identity carrier (deny-by-default).");

            foreach (var statusCode in extraProblemStatusCodes)
            {
                operation.Responses[statusCode.ToString(CultureInfo.InvariantCulture)] =
                    ProblemResponse(ProblemDescriptions[statusCode]);
            }
        }
    }

    /// <summary>The documented purpose of each exception-driven status code stamped by the transformer.</summary>
    private static readonly Dictionary<int, string> ProblemDescriptions = new()
    {
        [404] = "The task does not exist, is soft-deleted, or belongs to another user (errorCode = not_found).",
        [409] = "The write carried a stale version (optimistic-concurrency conflict; errorCode = version_conflict).",
        [422] = "Request validation failed (errorCode = validation_failed).",
    };

    /// <summary>A ProblemDetails-bodied response referencing the shared <c>ProblemDetails</c> schema.</summary>
    private static OpenApiResponse ProblemResponse(string description) => new()
    {
        Description = description,
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
