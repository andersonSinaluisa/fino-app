using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Abstractions;
using Nexo.Application.EmailIngestion;
using Nexo.Domain.Common;
using Nexo.Infrastructure.Email;
using Nexo.Infrastructure.Persistence;
using Nexo.Infrastructure.Persistence.Seeding;
using Nexo.Infrastructure.Push;
using Nexo.Infrastructure.Realtime;
using Nexo.Infrastructure.Security;

namespace Nexo.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNexoInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<SecretProtectionOptions>(configuration.GetSection(SecretProtectionOptions.SectionName));
        services.Configure<EmailIngestionOptions>(configuration.GetSection(EmailIngestionOptions.SectionName));
        services.Configure<DemoSeedOptions>(configuration.GetSection(DemoSeedOptions.SectionName));

        var connectionString = configuration.GetConnectionString("Default")
                               ?? throw new InvalidOperationException(
                                   "ConnectionStrings:Default is required. See .env.example.");

        // ISecretProtector is only constructed once OAuth is wired up, so its own
        // constructor check would not run until then. The key also salts the audit
        // log's IP hashes, which happens on every sign-in — so validate it here.
        var encryptionKey = configuration[$"{SecretProtectionOptions.SectionName}:EncryptionKey"];
        if (string.IsNullOrWhiteSpace(encryptionKey))
        {
            throw new InvalidOperationException(
                "Nexo:Secrets:EncryptionKey is required. Generate one with: openssl rand -base64 32");
        }

        services.AddDbContext<NexoDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(NexoDbContext).Assembly.FullName);
                npgsql.EnableRetryOnFailure(3);
            });

            // Never leak parameter values (amounts, emails) into logs.
            options.EnableSensitiveDataLogging(false);
        });

        services.AddScoped<INexoDbContext>(sp => sp.GetRequiredService<NexoDbContext>());
        services.AddScoped<ReferenceDataSeeder>();
        services.AddScoped<DemoDataSeeder>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ISecretProtector, AesSecretProtector>();
        services.AddSingleton<IIpHasher, HmacIpHasher>();
        services.AddScoped<IAccessTokenService, JwtAccessTokenService>();
        services.AddSingleton<IEmailProviderAvailability, ConfigurationEmailProviderAvailability>();
        services.AddSingleton<IRealtimeNotifier, NullRealtimeNotifier>();

        AddPushSender(services, configuration);

        return services;
    }

    private static void AddPushSender(IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue("Nexo:Push:Enabled", false))
        {
            services.AddSingleton<IPushSender, NoOpPushSender>();
            return;
        }

        services.AddHttpClient<IPushSender, ExpoPushSender>(client =>
        {
            client.BaseAddress = new Uri("https://exp.host/");
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("accept-encoding", "gzip, deflate");
        });
    }
}
