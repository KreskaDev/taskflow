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
        // setTaskDone is membership-aware as of slice 005 (the BLOCKER-resolved deviation): a viewer member
        // mutating a shared task → 403, so it now carries the 403 like the new editor commands.
        SetOperation(document, "/api/tasks/{id}/status", OperationType.Patch, "setTaskDone", 403, 404, 409, 422);
        SetOperation(document, "/api/tasks/{id}/position", OperationType.Patch, "reorderTask", 404, 409, 422);
        SetOperation(document, "/api/tasks/{id}", OperationType.Delete, "deleteTask", 404);

        // Daily-planning surface (slice 005, contracts/openapi.yaml). The Today/Upcoming reads carry only the
        // deny-by-default 401 (a non-member's task is simply absent — no 403/404 on a collection read). The
        // three editor mutations dispatch by visibility: shared viewer → 403, foreign/non-member → 404, stale
        // version → 409, bad input → 422. NO new errorCode — the 403 reuses the pre-provisioned `forbidden` (R11).
        SetOperation(document, "/api/tasks/today", OperationType.Get, "getTodayTasks");
        SetOperation(document, "/api/tasks/upcoming", OperationType.Get, "getUpcomingTasks");
        SetOperation(document, "/api/tasks/{id}/priority", OperationType.Patch, "setTaskPriority", 403, 404, 409, 422);
        SetOperation(document, "/api/tasks/{id}/due-date", OperationType.Patch, "rescheduleTaskDueDate", 403, 404, 409, 422);
        SetOperation(document, "/api/tasks/{id}/edit", OperationType.Patch, "editTask", 403, 404, 409, 422);

        // Task-assignment surface (slice 008, contracts/openapi.yaml). getAssignedToMe is a caller-scoped read
        // (only the deny-by-default 401). setTaskAssignees dispatches by visibility: shared viewer → 403,
        // non-member / personal task / foreign → 404, non-member assignee / bad set → 422, stale → 409. NO new
        // errorCode — the ErrorCodes enum is UNCHANGED (R8).
        SetOperation(document, "/api/tasks/assigned", OperationType.Get, "getAssignedToMe");
        SetOperation(document, "/api/tasks/{id}/assignees", OperationType.Patch, "setTaskAssignees", 403, 404, 409, 422);

        // Labels surface (slice 006, contracts/openapi.yaml). listLabels is a caller-scoped read (only the
        // deny-by-default 401). createLabel is an idempotent PUT-upsert (no 409; dup name → 422). update/delete
        // are ownership-gated (not-owned/absent → 404; dup name on update → 422). setTaskLabels is TWO-SIDED and
        // VERSIONLESS (no 409): task write-access (viewer → 403, non-member/personal-foreign → 404) AND every
        // label owned by the caller (else 422). NO new errorCode — the ErrorCodes enum is UNCHANGED (R9).
        SetOperation(document, "/api/labels", OperationType.Get, "listLabels");
        SetOperation(document, "/api/labels/{id}", OperationType.Put, "createLabel", 422);
        SetOperation(document, "/api/labels/{id}", OperationType.Patch, "updateLabel", 404, 422);
        SetOperation(document, "/api/labels/{id}", OperationType.Delete, "deleteLabel", 404);
        SetOperation(document, "/api/tasks/{id}/labels", OperationType.Patch, "setTaskLabels", 403, 404, 422);

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

        // Sharing / membership surface (slice 007, contracts/openapi.yaml). Same pattern: the success bodies
        // auto-emit from the endpoint signatures; here we stamp the stable operationIds and the
        // exception-driven ProblemDetails responses (401 always, plus 403/404/409/422 per op). This slice is
        // the FIRST USE of the pre-provisioned `forbidden` (403) and `last_owner` (409, an errorCode branch
        // of the shared Conflict response) — the ErrorCodes enum is UNCHANGED (R16). share is owner-only on a
        // still-personal project (non-owner → 404, never 403); leave is self-service (no 403 — a non-member
        // is 404). The 409 covers version_conflict everywhere and additionally last_owner on
        // change-role/remove/leave (clients branch on errorCode, ADR-0009).
        SetOperation(document, "/api/projects/{id}/share", OperationType.Patch, "shareProject", 404, 409, 422);
        SetOperation(document, "/api/projects/{id}/unshare", OperationType.Patch, "unshareProject", 403, 404, 409);
        SetOperation(document, "/api/projects/{id}/owner", OperationType.Patch, "transferProjectOwnership", 403, 404, 409, 422);
        SetOperation(document, "/api/projects/{id}/members", OperationType.Get, "listProjectMembers", 404);
        SetOperation(document, "/api/projects/{id}/members", OperationType.Post, "inviteProjectMember", 403, 404, 409, 422);
        SetOperation(document, "/api/projects/{id}/members/{userId}", OperationType.Patch, "changeProjectMemberRole", 403, 404, 409, 422);
        SetOperation(document, "/api/projects/{id}/members/{userId}", OperationType.Delete, "removeProjectMember", 403, 404, 409);
        SetOperation(document, "/api/projects/{id}/membership", OperationType.Delete, "leaveProject", 404, 409);

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
        [403] = "The caller is a member but lacks the required role for this operation (errorCode = forbidden).",
        [404] = "The resource does not exist, is soft-deleted, or the caller is not permitted to observe it (errorCode = not_found).",
        [409] = "A state conflict: a stale version (errorCode = version_conflict) or the last-owner guard (errorCode = last_owner).",
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
