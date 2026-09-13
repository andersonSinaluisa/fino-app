using Microsoft.EntityFrameworkCore;
using Nexo.Domain.Accounts;
using Nexo.Domain.Audit;
using Nexo.Domain.Categories;
using Nexo.Domain.EmailIngestion;
using Nexo.Domain.Imports;
using Nexo.Domain.Insights;
using Nexo.Domain.Notifications;
using Nexo.Domain.Providers;
using Nexo.Domain.Pulses;
using Nexo.Domain.Transactions;
using Nexo.Domain.Users;

namespace Nexo.Application.Abstractions;

/// <summary>
/// Persistence surface used by the application layer. Every user-owned set already
/// has a per-user filter applied by the implementation, so handlers cannot forget it.
/// </summary>
public interface INexoDbContext
{
    DbSet<User> Users { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<Provider> Providers { get; }

    DbSet<FinancialAccount> FinancialAccounts { get; }

    DbSet<Transaction> Transactions { get; }

    DbSet<Category> Categories { get; }

    DbSet<CategorizationRule> CategorizationRules { get; }

    DbSet<CategoryCorrection> CategoryCorrections { get; }

    DbSet<Import> Imports { get; }

    DbSet<ImportRow> ImportRows { get; }

    DbSet<ImportColumnMapping> ImportColumnMappings { get; }

    DbSet<EmailConnection> EmailConnections { get; }

    DbSet<TrustedSender> TrustedSenders { get; }

    DbSet<Insight> Insights { get; }

    DbSet<Device> Devices { get; }

    DbSet<Notification> Notifications { get; }

    /// <summary>PULSO: proactive, explainable observations. See FinancialPulse's remarks.</summary>
    DbSet<FinancialPulse> Pulses { get; }

    DbSet<AuditLogEntry> AuditLog { get; }

    /// <summary>Escape hatch for the few queries that must see every user's rows (workers, admin jobs).</summary>
    IQueryable<T> IgnoringUserFilter<T>()
        where T : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
