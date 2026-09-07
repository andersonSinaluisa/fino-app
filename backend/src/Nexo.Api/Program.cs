using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nexo.Api.Endpoints;
using Nexo.Api.Setup;
using Nexo.Api.Realtime;
using Nexo.Application;
using Nexo.Application.Abstractions;
using Nexo.Infrastructure;
using Nexo.Infrastructure.Persistence;
using Nexo.Infrastructure.Persistence.Seeding;
using Nexo.Infrastructure.Security;
using Nexo.Workers;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Logging: JSON to stdout so any log pipeline can consume it. Scopes carry the
// correlation id. Nothing here ever writes a token or a movement.
// ---------------------------------------------------------------------------
builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    });
}
else
{
    builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

builder.Services.AddNexoApplication(builder.Configuration);
builder.Services.AddNexoInfrastructure(builder.Configuration);
builder.Services.AddNexoWorkers(builder.Configuration);

// SignalR replaces the no-op notifier registered by Infrastructure.
builder.Services.AddSignalR();
builder.Services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();

// ---------------------------------------------------------------------------
// Authentication
//
// The signing key is read through IOptions<JwtOptions>, never captured here.
// `builder.Configuration` before `builder.Build()` only sees the application's own
// providers: a test host, or any provider added later in the host pipeline, layers
// its values on afterwards. Capturing the key eagerly made the API *sign* tokens
// with one key and *validate* them with another, so every authenticated request
// answered 401 while sign-in itself looked healthy.
// ---------------------------------------------------------------------------
builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .PostConfigure<IHostEnvironment>((options, environment) =>
    {
        if (options.SigningKey.Length >= 32 || !environment.IsDevelopment())
        {
            return;
        }

        // Deterministic only so a fresh clone runs; every other environment is
        // rejected by the validation below, at startup rather than at first login.
        options.SigningKey = "nexo-development-signing-key-change-me-please";
    })
    .Validate(
        options => options.SigningKey.Length >= 32,
        "Nexo:Jwt:SigningKey must be set to at least 32 characters outside Development.")
    .ValidateOnStart();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, accessor) =>
    {
        var jwt = accessor.Value;

        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        // SignalR cannot send an Authorization header on the websocket handshake.
        bearer.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken)
                    && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// Rate limiting
// ---------------------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(RateLimitPolicies.Authentication, http =>
        RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(http),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    options.AddPolicy(RateLimitPolicies.Uploads, http =>
        RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(http),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
            }));

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(http),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    static string PartitionKey(HttpContext http) =>
        http.User.Identity?.IsAuthenticated == true
            ? $"user:{http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value}"
            : $"ip:{http.Connection.RemoteIpAddress}";
});

// ---------------------------------------------------------------------------
// Cross-cutting
// ---------------------------------------------------------------------------
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

builder.Services.AddCors(options => options.AddPolicy("mobile", policy =>
{
    var origins = builder.Configuration.GetSection("Nexo:Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (origins.Length > 0)
    {
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    }
    else if (builder.Environment.IsDevelopment())
    {
        // Expo Go talks to the API from a device on the LAN; there is no browser
        // origin to pin in development.
        policy.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    }
    else
    {
        // Entregable 20: forgetting Nexo:Cors:AllowedOrigins outside Development
        // must fail closed (no cross-origin browser access at all), not fall
        // back to "any origin" -- the same fail-closed shape already used for
        // Nexo:Jwt:SigningKey above. Native mobile clients are unaffected: CORS
        // is a browser-enforced mechanism, so Expo/React Native requests never
        // go through it in the first place.
        policy.WithOrigins([]).AllowAnyHeader().AllowAnyMethod();
    }
}));

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024;
});

var app = builder.Build();

// Behind a reverse proxy the socket address is the proxy's. Without this the
// anonymous rate-limit partition and the audit-log IP hash collapse to one value.
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};

// The defaults trust only loopback, so in a container the header is discarded and
// every client collapses into one rate-limit bucket. Clearing the lists trusts the
// hop in front of us, which is correct when the app is only reachable through it.
forwardedHeaders.KnownNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    // Minimal, boring security headers. The API serves JSON only, so there is
    // nothing for a browser to render: CSP and Permissions-Policy are pure
    // defense in depth for the one page that does exist (the Development-only
    // OpenAPI UI below), not something an actual attack surface depends on.
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    context.Response.Headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";
    await next();
});

app.UseCors("mobile");

// Authentication first: the rate-limit partition key reads HttpContext.User, which
// is still anonymous if the limiter runs before the JWT has been validated.
app.UseAuthentication();

// The policies are always registered (endpoints reference them by name); only the
// middleware is optional, so an automated suite can hammer /auth without tripping
// the brute-force budget.
if (app.Configuration.GetValue("Nexo:RateLimiting:Enabled", true))
{
    app.UseRateLimiter();
}

app.UseAuthorization();

app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapTransactionEndpoints();
app.MapAnalyticsEndpoints();
app.MapInternalTransferEndpoints();
app.MapImportEndpoints();
app.MapCategorizationRuleEndpoints();
app.MapProfileEndpoints();
app.MapEmailIngestionEndpoints();

app.MapHub<NexoHub>("/hubs/nexo");

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await InitialiseDatabaseAsync(app);

await app.RunAsync();

static async Task InitialiseDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    var db = services.GetRequiredService<NexoDbContext>();

    var autoMigrate = app.Configuration.GetValue("Nexo:Database:AutoMigrate", app.Environment.IsDevelopment());
    if (autoMigrate)
    {
        // GetMigrations() reads the assembly, not the database, so this decision
        // works on an empty server. Production always ships migrations; a fresh
        // clone before the first `dotnet ef migrations add` still boots.
        if (db.Database.GetMigrations().Any())
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            logger.LogWarning(
                "No EF migrations found; creating the schema from the model. Run 'dotnet ef migrations add InitialCreate' before deploying.");
            await db.Database.EnsureCreatedAsync();
        }

        logger.LogInformation("Database schema is up to date.");
    }

    await services.GetRequiredService<ReferenceDataSeeder>().SeedAsync();

    var demo = app.Configuration.GetValue("Nexo:Seed:Demo", false);
    if (demo && app.Environment.IsDevelopment())
    {
        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DemoSeedOptions>>().Value;
        await services.GetRequiredService<DemoDataSeeder>().SeedAsync(options);
    }
    else if (demo)
    {
        logger.LogWarning("Nexo:Seed:Demo is enabled but the environment is not Development; demo data was NOT seeded.");
    }
}

/// <summary>Exposed so the integration test host can reference the entry point.</summary>
public partial class Program;
