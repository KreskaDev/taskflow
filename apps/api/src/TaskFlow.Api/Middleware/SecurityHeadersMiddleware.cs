namespace TaskFlow.Api.Middleware;

/// <summary>
/// Adds the standard security response headers (FR-099, Constitution XII) to every API response,
/// in all environments (wired unconditionally in <c>Program.cs</c>).
/// The API serves only JSON to the internal BFF (no HTML, no browser-executed content),
/// so the CSP is maximally locked down (<c>default-src 'none'</c>). The user-facing CSP
/// for rendered pages is owned by the Next.js BFF.
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Apply on the way out via OnStarting so the headers survive a later
        // Response.Clear() (e.g. the error path in ProblemDetailsMiddleware) and are
        // present on EVERY response, including non-2xx.
        context.Response.OnStarting(static state =>
        {
            var headers = ((HttpContext)state).Response.Headers;
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            return Task.CompletedTask;
        }, context);

        return next(context);
    }
}
