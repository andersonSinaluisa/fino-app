using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Nexo.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> so migrations can be generated without booting the API.
/// Reads the connection string from ConnectionStrings__Default, falling back to the
/// local Docker Compose database.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NexoDbContext>
{
    private const string Fallback =
        "Host=localhost;Port=5432;Database=nexo;Username=nexo;Password=nexo_local_dev";

    public NexoDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? Environment.GetEnvironmentVariable("NEXO_CONNECTION_STRING")
            ?? Fallback;

        var options = new DbContextOptionsBuilder<NexoDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(NexoDbContext).Assembly.FullName))
            .Options;

        return new NexoDbContext(options, new NullCurrentUser());
    }
}
