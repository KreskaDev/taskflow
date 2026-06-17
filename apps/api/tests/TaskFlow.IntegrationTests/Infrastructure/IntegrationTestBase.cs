using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace TaskFlow.IntegrationTests.Infrastructure;

/// <summary>
/// Base for integration tests: spins up a throwaway PostgreSQL 17 container
/// (Testcontainers) and boots the real API host (WebApplicationFactory) against it.
/// EF migrations + Wolverine durable storage are provisioned on host startup, so each
/// test class runs against a fresh, fully-migrated schema including the tombstone seed.
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    /// <summary>
    /// Fixed HS256 signing key shared by the booted API and <see cref="TestJwtHelper"/>
    /// so minted carriers validate. HMAC-SHA256 requires ≥ 256 bits (32 bytes).
    /// </summary>
    public const string SigningKey = "taskflow-integration-test-signing-key-0123456789";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>HTTP client bound to the in-memory test server.</summary>
    protected HttpClient Client { get; private set; } = null!;

    /// <summary>The booted host's service provider, for resolving scoped services (e.g. the DbContext).</summary>
    protected IServiceProvider Services => _factory.Services;

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The factory is stored in a field and disposed in DisposeAsync (IAsyncLifetime).")]
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("ConnectionStrings:postgres", _postgres.GetConnectionString());
                b.UseSetting("Jwt:SigningKey", SigningKey);
                b.UseEnvironment("Development");

                // The default Windows EventLog logger provider intermittently throws `OpenForWrite`
                // during host disposal (a teardown race surfacing as a flaky DisposeAsync failure).
                // Tests never read logs, so clear all providers — this makes the suite deterministic.
                b.ConfigureLogging(logging => logging.ClearProviders());
            });

        // Forces host construction → EF Migrate + Wolverine resource setup run now.
        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>Sends a request carrying a Bearer identity carrier (allow tests).</summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The request (and its content) must outlive the returned send Task — TestServer ties the response body stream to the request lifetime — so it is intentionally left to GC rather than disposed here.")]
    protected Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? jsonBody = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (jsonBody is not null)
        {
            request.Content = JsonContent.Create(jsonBody);
        }

        return Client.SendAsync(request);
    }
}
