using System.Net;
using FluentAssertions;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests;

/// <summary>
/// Regression lock for the API security-header baseline (FR-099, Constitution XII). The headers are
/// emitted via <c>Response.OnStarting</c> so they must survive the error path's <c>Response.Clear()</c>
/// in <c>ProblemDetailsMiddleware</c>. These tests assert the headers are present on both a denied
/// (401, error path) and a successful (200) response, so a regression that removes or reorders
/// <c>SecurityHeadersMiddleware</c> fails loudly instead of slipping through the rest of the suite.
/// </summary>
public sealed class SecurityHeadersTests : IntegrationTestBase
{
    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Content-Security-Policy", out var csp).Should().BeTrue();
        csp.Should().ContainSingle().Which.Should().Be("default-src 'none'; frame-ancestors 'none'");

        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle().Which.Should().Be("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle().Which.Should().Be("no-referrer");
        response.Headers.GetValues("Cross-Origin-Resource-Policy").Should().ContainSingle().Which.Should().Be("same-origin");
    }

    [Fact]
    public async Task Security_headers_survive_the_401_error_path()
    {
        // Unauthenticated → 401 via ProblemDetailsMiddleware, which Response.Clear()s; the headers
        // (set via OnStarting) must still be present on the cleared error response.
        using var response = await Client.GetAsync(new Uri("/api/users/me", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task Security_headers_are_present_on_a_200_response()
    {
        using var response = await Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertSecurityHeaders(response);
    }
}
