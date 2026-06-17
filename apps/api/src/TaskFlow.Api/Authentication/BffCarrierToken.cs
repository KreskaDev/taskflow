namespace TaskFlow.Api.Authentication;

/// <summary>
/// The single source of truth for the BFF→API identity-carrier JWT contract
/// (R3, ADR-0004). The API's JWT bearer validation (Program.cs), the integration
/// tests' <c>TestJwtHelper</c>, and the BFF's <c>token.ts</c> minting MUST all
/// agree on these values byte-for-byte.
/// </summary>
internal static class BffCarrierToken
{
    /// <summary>HMAC-SHA256 — symmetric, shared <c>JWT_SIGNING_KEY</c> over the internal network.</summary>
    public const string Algorithm = "HS256";

    /// <summary>Token issuer (the BFF).</summary>
    public const string Issuer = "taskflow-bff";

    /// <summary>Token audience (this API).</summary>
    public const string Audience = "taskflow-api";

    /// <summary>Subject claim — the caller's TaskFlow user id (or, on the bootstrap ensure call, the Google subject id).</summary>
    public const string SubjectClaim = "sub";

    /// <summary>Email claim.</summary>
    public const string EmailClaim = "email";

    /// <summary>Display-name claim.</summary>
    public const string NameClaim = "name";
}
