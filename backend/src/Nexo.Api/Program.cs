using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
// ---------------------------------------------------------------------------
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwt = jwtSection.Get<JwtOptions>() ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
{
    if (builder.Environment.IsDevelopment())
    {
        // Deterministic only so a fresh clone runs; production fails fast below.
        jwt.SigningKey = "nexo-development-signing-key-change-me-please";
        builder.Configuration["Nexo:Jwt:SigningKey"] = jwt.SigningKey;
    }
    else
    {
        throw new InvalidOperationException(
            "Nexo:Jwt:SigningKey must be set to at least 32 characters outside Development.");
    }
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
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
        options.Events = new JwtBearerEvents
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
    else
    {
        // Expo Go talks to the API from a device on the LAN; there is no browser
        // origin to pin in development.
        policy.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
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
    // Minimal, boring security headers. The API serves JSON only.
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    await next();
});

app.UseCors("mobile");

// Authentication first: the rate-limit partition key reads HttpContext.User, which
// is still anonymous if the limiter runs before the JWT has been validated.
app.UseAuthentication();

// The policies are always registered (endpoints reference them by name); only the
// middleware is optional, so an automated suite can hammer /auth without tripping
// the brute-force budget.
if (builder.Configuration.GetValue("Nexo:RateLimiting:Enabled", true))
{
    app.UseRateLimiter();
}

app.UseAuthorization();

app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapTransactionEndpoints();
app.MapImportEndpoints();
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
