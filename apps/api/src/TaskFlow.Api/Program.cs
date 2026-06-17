using System.Text;
using JasperFx.Resources;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TaskFlow.Api.Authentication;
using TaskFlow.Api.Middleware;
using TaskFlow.Application.Authorization;
using TaskFlow.Application.IdentityAccess;
using TaskFlow.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Http;
using Wolverine.Postgresql;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:postgres is not configured.");

var signingKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Jwt:SigningKey (JWT_SIGNING_KEY) is not configured.");

// --- Authentication: validate the BFF-minted HMAC-SHA256 identity carrier (R3/ADR-0004) ---
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep raw "sub"/"email"/"name" claims instead of the legacy SOAP claim-type mapping.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = BffCarrierToken.Issuer,
            ValidateAudience = true,
            ValidAudience = BffCarrierToken.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.FromSeconds(5),
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddScoped<IResourceAuthorizationPolicy, ResourceAuthorizationPolicy>();
builder.Services.AddScoped<IUserRepository, UserRepository>();

// --- EF Core write-side + Wolverine durable messaging (Postgres outbox/inbox) ---
builder.Services.AddDbContextWithWolverineIntegration<AppDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddResourceSetupOnStartup();

builder.Host.UseWolverine(opts =>
{
    // Command/query handlers live in TaskFlow.Application, not the API entry assembly, so they
    // must be explicitly included for Wolverine's handler discovery to find them.
    opts.Discovery.IncludeAssembly(typeof(ICurrentUser).Assembly);

    opts.PersistMessagesWithPostgresql(connectionString);
    opts.UseEntityFrameworkCoreTransactions();
    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableLocalQueues();

    // Single-node deployment (one VPS, ~10 users): Solo durability skips the distributed
    // leader-election/agent machinery, giving faster, cleaner startup/shutdown (also avoids a
    // background-agent logger race during WebApplicationFactory teardown in integration tests).
    opts.Durability.Mode = DurabilityMode.Solo;

    // Deny-by-default authentication on EVERY handler (FR-068). Verified at runtime
    // by the Phase 3 deny tests — a green build does not prove Wolverine wove it.
    // (typeof overload: AuthorizationMiddleware is a static class.)
    opts.Policies.AddMiddleware(typeof(AuthorizationMiddleware));
});

// --- Built-in .NET 9 OpenAPI document at /openapi/v1.json (R5) ---
builder.Services.AddOpenApi();
builder.Services.AddWolverineHttp();

// No CORS: the API is single-origin (only the BFF on the internal Docker network calls it).

var app = builder.Build();

// EF Core migrations are the schema source of truth (Constitution VI): apply on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync().ConfigureAwait(false);
}

// Outermost: map any unhandled exception to an RFC 9457 ProblemDetails response (ADR-0009).
app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();

// Deny-by-default backstop at the HTTP layer (defense in depth behind the message-pipeline
// middleware): every Wolverine.Http endpoint — including any future endpoint that does work
// inline instead of delegating to the bus — is gated by AuthorizationMiddleware.Before, which
// throws UnauthenticatedException (→ 401 via ProblemDetailsMiddleware) for an unauthenticated caller.
app.MapWolverineEndpoints(opts => opts.AddMiddleware(typeof(AuthorizationMiddleware)));

await app.RunAsync().ConfigureAwait(false);
