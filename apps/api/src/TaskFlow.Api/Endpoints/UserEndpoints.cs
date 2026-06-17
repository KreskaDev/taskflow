using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.IdentityAccess;
using TaskFlow.Application.IdentityAccess.Commands;
using TaskFlow.Application.IdentityAccess.Queries;
using Wolverine;
using Wolverine.Http;

namespace TaskFlow.Api.Endpoints;

/// <summary>
/// HTTP surface for the User aggregate (contracts/openapi.yaml). Each endpoint is a thin transport
/// adapter that dispatches the corresponding command/query through Wolverine's local message pipeline
/// via <see cref="IMessageBus.InvokeAsync{T}"/>, so the deny-by-default authorization middleware (T019)
/// is woven ahead of every handler.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine.Http discovers and maps HTTP endpoints only on public types; this class must stay public.")]
public static class UserEndpoints
{
    /// <summary>Create or match a user from the Google identity (BFF OAuth-callback bootstrap).</summary>
    [WolverinePost("/api/users/ensure")]
    public static Task<UserProfile> Ensure(EnsureUser command, IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<UserProfile>(command);
    }

    /// <summary>Return the current caller's profile.</summary>
    [WolverineGet("/api/users/me")]
    public static Task<UserProfile> Me(IMessageBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.InvokeAsync<UserProfile>(new GetCurrentUser());
    }
}
