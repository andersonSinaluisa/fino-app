using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Nexo.Workers;

public static class WorkerRegistration
{
    public static IServiceCollection AddNexoWorkers(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WorkerOptions>(configuration.GetSection(WorkerOptions.SectionName));
        services.AddHostedService<InsightRefreshWorker>();
        services.AddHostedService<PulseEvaluationWorker>();
        services.AddHostedService<AccountDeletionWorker>();
        return services;
    }
}
