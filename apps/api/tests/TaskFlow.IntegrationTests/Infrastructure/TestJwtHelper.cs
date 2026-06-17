using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TaskFlow.Api.Authentication;

namespace TaskFlow.IntegrationTests.Infrastructure;

/// <summary>
/// Mints BFF-style identity carriers for allow/deny tests (SC-016). Uses the exact
/// <see cref="BffCarrierToken"/> contract the API validates against, so a token from
/// <see cref="Valid"/> authenticates and one from <see cref="WrongKey"/> / <see cref="Expired"/> is rejected (401).
/// </summary>
public static class TestJwtHelper
{
    /// <summary>A valid carrier: correct key, issuer, audience, 60-second lifetime.</summary>
    public static string Valid(string subject, string email = "user@example.com", string name = "Test User") =>
        Create(subject, email, name, IntegrationTestBase.SigningKey, TimeSpan.FromSeconds(60));

    /// <summary>Signed with the wrong key — signature validation must fail (deny).</summary>
    public static string WrongKey(string subject, string email = "user@example.com", string name = "Test User") =>
        Create(subject, email, name, "this-is-the-wrong-signing-key-0123456789abcd", TimeSpan.FromSeconds(60));

    /// <summary>Already expired — lifetime validation must fail (deny).</summary>
    public static string Expired(string subject, string email = "user@example.com", string name = "Test User") =>
        Create(subject, email, name, IntegrationTestBase.SigningKey, TimeSpan.FromMinutes(-5));

    private static string Create(string subject, string email, string name, string signingKey, TimeSpan lifetime)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Fixed reference instant avoids a clock dependency; lifetime is relative to it.
        var issuedAt = DateTime.UtcNow;
        var expires = issuedAt.Add(lifetime);

        // notBefore must be strictly before expires. For an already-expired token (negative lifetime),
        // back-date notBefore a further minute so the token is well-formed but past its lifetime.
        var notBefore = lifetime < TimeSpan.Zero ? expires.AddMinutes(-1) : issuedAt;

        var token = new JwtSecurityToken(
            issuer: BffCarrierToken.Issuer,
            audience: BffCarrierToken.Audience,
            claims:
            [
                new Claim(BffCarrierToken.SubjectClaim, subject),
                new Claim(BffCarrierToken.EmailClaim, email),
                new Claim(BffCarrierToken.NameClaim, name),
            ],
            notBefore: notBefore,
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
