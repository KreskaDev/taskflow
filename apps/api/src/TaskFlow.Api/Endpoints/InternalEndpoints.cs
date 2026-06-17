using System.Diagnostics.CodeAnalysis;
using Wolverine.Http;

namespace TaskFlow.Api.Endpoints;

/// <summary>
/// Internal, non-product endpoints for the BFF/ops on the internal network.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine.Http discovers and maps HTTP endpoints only on public types.")]
public static class InternalEndpoints
{
    /// <summary>
    /// Confirms a caller's BFF carrier authenticates (returns 204 when it does). Deliberately does
    /// NOT delegate to the message bus — so it is gated solely by the HTTP-layer deny-by-default
    /// backstop (<c>MapWolverineEndpoints(... AddMiddleware(AuthorizationMiddleware) ...)</c>),
    /// keeping that backstop under a standing test: an unauthenticated call must be rejected 401
    /// even though no handler/middleware runs in the endpoint body.
    /// </summary>
    [WolverineGet("/api/internal/auth-check")]
    public static IResult AuthCheck() => Results.NoContent();
}
