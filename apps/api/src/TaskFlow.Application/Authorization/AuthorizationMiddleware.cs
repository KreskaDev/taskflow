namespace TaskFlow.Application.Authorization;

/// <summary>
/// Wolverine pipeline middleware that enforces deny-by-default authentication on
/// <b>every</b> command/query handler (FR-068). Registered globally via
/// <c>opts.Policies.AddMiddleware&lt;AuthorizationMiddleware&gt;()</c> so the
/// <see cref="Before"/> method is woven ahead of each handler.
/// </summary>
/// <remarks>
/// This is the application-layer guarantee that a handler never runs for an
/// unauthenticated caller; it complements (and is defense-in-depth behind) the
/// HTTP-layer JWT bearer authentication. Per-resource ownership/membership is
/// enforced inside handlers via <see cref="IResourceAuthorizationPolicy"/>.
///
/// Wolverine recognizes <c>Before</c> by convention (case-sensitive); throwing
/// here aborts the handler and surfaces as an RFC 9457 error (ADR-0009).
/// </remarks>
public static class AuthorizationMiddleware
{
    /// <summary>
    /// Runs before every handler. Throws <see cref="UnauthenticatedException"/>
    /// (→ HTTP 401) when no valid authenticated principal is present.
    /// </summary>
    public static void Before(ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        if (!currentUser.IsAuthenticated)
        {
            throw new UnauthenticatedException();
        }
    }
}
