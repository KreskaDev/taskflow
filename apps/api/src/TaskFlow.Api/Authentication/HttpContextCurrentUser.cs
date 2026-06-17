using System.Diagnostics.CodeAnalysis;
using TaskFlow.Application.Authorization;
using TaskFlow.Domain.IdentityAccess;

namespace TaskFlow.Api.Authentication;

/// <summary>
/// API-layer adapter exposing the JWT principal as the application-layer
/// <see cref="ICurrentUser"/>. <see cref="IsAuthenticated"/> reflects whether a
/// valid carrier was presented (true even for the bootstrap ensure call, whose
/// <c>sub</c> is a Google subject id rather than a TaskFlow user id); <see cref="Id"/>
/// is only meaningful when <c>sub</c> is a TaskFlow user id (GUID).
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal",
    Justification = "Wolverine weaves this into handler code generation as the ICurrentUser dependency; the concrete type must be public for inline (non-service-located) resolution.")]
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated =>
        accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public UserId Id =>
        TryGetId(out var id)
            ? id
            : throw new InvalidOperationException("No authenticated TaskFlow user id is present on the current request.");

    private bool TryGetId(out UserId id)
    {
        id = default;
        var sub = accessor.HttpContext?.User.FindFirst(BffCarrierToken.SubjectClaim)?.Value;
        if (Guid.TryParse(sub, out var guid))
        {
            id = UserId.From(guid);
            return true;
        }

        return false;
    }
}
