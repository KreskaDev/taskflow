using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Logging;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.Errors;

namespace TaskFlow.Api.Middleware;

/// <summary>
/// Translates unhandled exceptions into RFC 9457 ProblemDetails responses carrying
/// the stable <c>errorCode</c> enum from contracts/openapi.yaml (ADR-0009). This is
/// the outermost application middleware so it wraps the whole pipeline.
/// </summary>
[SuppressMessage("Performance", "CA1848:Use the LoggerMessage delegates",
    Justification = "Error-path logging only (never the hot path); the readability of structured calls outweighs the source-generator ceremony here.")]
internal sealed class ProblemDetailsMiddleware(RequestDelegate next, ILogger<ProblemDetailsMiddleware> logger)
{
    private const string ContentType = "application/problem+json";
    private const string TypeBaseUri = "https://taskflow.example/errors/";

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "This is the global exception boundary; every unhandled exception must be mapped to a ProblemDetails response.")]
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                // Cannot rewrite a response already on the wire; re-throw for the host to log.
                logger.LogError(ex, "Exception after response started; cannot write ProblemDetails.");
                throw;
            }

            await WriteProblemAsync(context, ex).ConfigureAwait(false);
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception ex)
    {
        var (status, errorCode, title, errors) = Map(ex);

        // Structured logging without secrets (FR-050): log the type/code/path, never the token.
        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(ex, "Unhandled error {ErrorCode} on {Method} {Path}", errorCode, context.Request.Method, context.Request.Path);
        }
        else
        {
            logger.LogInformation("Request rejected {ErrorCode} ({Status}) on {Method} {Path}", errorCode, status, context.Request.Method, context.Request.Path);
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = ContentType;

        var payload = new Dictionary<string, object?>
        {
            ["type"] = TypeBaseUri + errorCode,
            ["title"] = title,
            ["status"] = status,
            ["errorCode"] = errorCode,
            ["instance"] = context.Request.Path.Value,
        };

        if (errors is not null)
        {
            payload["errors"] = errors;
        }

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, SerializerOptions)).ConfigureAwait(false);
    }

    private static (int Status, string ErrorCode, string Title, Dictionary<string, string[]>? Errors) Map(Exception ex) => ex switch
    {
        UnauthenticatedException => (StatusCodes.Status401Unauthorized, "unauthenticated", "Authentication required", null),
        ForbiddenException => (StatusCodes.Status403Forbidden, "forbidden", "Access denied", null),
        NotFoundException => (StatusCodes.Status404NotFound, "not_found", "Resource not found", null),
        VersionConflictException => (StatusCodes.Status409Conflict, "version_conflict", "Version conflict", null),
        ValidationException ve => (StatusCodes.Status422UnprocessableEntity, "validation_failed", "Validation failed", ToErrors(ve)),
        _ => (StatusCodes.Status500InternalServerError, "internal_error", "An unexpected error occurred", null),
    };

    private static Dictionary<string, string[]> ToErrors(ValidationException ve) =>
        ve.Errors
            .GroupBy(e => e.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).ToArray(),
                StringComparer.Ordinal);
}
