using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexo.Domain.Common;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Boots the real API — real endpoints, real authentication, real handlers — against
/// a throwaway SQLite database. No Docker, no Postgres, so `dotnet test` works on a
/// clean machine and in CI. Provider-specific SQL is avoided in the model precisely
/// so this stays honest; the Postgres path is covered by running the app.
/// </summary>
public sealed class NexoApiFactory : WebApplicationFactory<Program>
{
    /// <summary>The instant every test runs at. Matches the statement fixtures.</summary>
    public static readonly DateTimeOffset Now = new(2026, 3, 10, 17, 0, 0, TimeSpan.Zero);

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"nexo-tests-{Guid.CreateVersion7():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={_databasePath}",
                ["Nexo:Jwt:SigningKey"] = "integration-tests-signing-key-at-least-32-chars",
                ["Nexo:Secrets:EncryptionKey"] = Convert.ToBase64String(new byte[32]),
                ["Nexo:Seed:Demo"] = "false",
                ["Nexo:Workers:Enabled"] = "false",
                ["Nexo:Push:Enabled"] = "false",
                ["Nexo:Database:AutoMigrate"] = "true",

                // The suite registers dozens of users in a few seconds; the
                // brute-force budget would reject most of them. The limiter itself
                // is exercised by configuration, not by every test.
                ["Nexo:RateLimiting:Enabled"] = "false",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Swap the Npgsql registration for SQLite. Every EF options descriptor
            // has to go, not just DbContextOptions<T>.
            services.RemoveAll<DbContextOptions<NexoDbContext>>();
            services.RemoveAll<DbContextOptions>();

            foreach (var descriptor in services
                         .Where(d => d.ServiceType.FullName?.Contains("IDbContextOptionsConfiguration", StringComparison.Ordinal) == true)
                         .ToList())
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<NexoDbContext>(options =>
                options.UseSqlite($"Data Source={_databasePath}"));

            // Fixtures are dated March 2026. Without a fixed clock, assertions about
            // "this month" would pass or fail depending on the day the suite runs.
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(new FixedClock(Now));
        });
    }

    /// <summary>Building the server runs migrations/EnsureCreated and the reference seed.</summary>
    public void EnsureStarted() => _ = Server;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing || !File.Exists(_databasePath))
        {
            return;
        }

        try
        {
            File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a test run over.
        }
    }
}
