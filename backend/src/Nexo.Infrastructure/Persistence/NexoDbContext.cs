using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Domain.Accounts;
using Nexo.Domain.Audit;
using Nexo.Domain.Categories;
using Nexo.Domain.Common;
using Nexo.Domain.EmailIngestion;
using Nexo.Domain.Imports;
using Nexo.Domain.Insights;
using Nexo.Domain.Notifications;
using Nexo.Domain.Providers;
using Nexo.Domain.Transactions;
using Nexo.Domain.Users;

namespace Nexo.Infrastructure.Persistence;

public sealed class NexoDbContext : DbContext, INexoDbContext
{
    private readonly ICurrentUser _currentUser;

    // A single constructor keeps EF's constructor resolution unambiguous.
    // Hosts that have no request user (workers, design time, seeding) register
    // NullCurrentUser, which leaves the query filters inert.
    public NexoDbContext(DbContextOptions<NexoDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    /// <summary>
    /// Read by the global query filters. Null means "no ambient user" (workers,
    /// seeding, design-time), in which case the filters do not restrict anything;
    /// request handlers always pass an explicit UserId predicate as well, so tenant
    /// isolation never depends on this alone.
    /// </summary>
    public Guid? CurrentUserId => _currentUser.UserId;

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Provider> Providers => Set<Provider>();

    public DbSet<FinancialAccount> FinancialAccounts => Set<FinancialAccount>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<CategorizationRule> CategorizationRules => Set<CategorizationRule>();

    public DbSet<CategoryCorrection> CategoryCorrections => Set<CategoryCorrection>();

    public DbSet<Import> Imports => Set<Import>();

    public DbSet<ImportRow> ImportRows => Set<ImportRow>();

    public DbSet<EmailConnection> EmailConnections => Set<EmailConnection>();

    public DbSet<TrustedSender> TrustedSenders => Set<TrustedSender>();

    public DbSet<Insight> Insights => Set<Insight>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    public IQueryable<T> IgnoringUserFilter<T>()
        where T : class => Set<T>().IgnoreQueryFilters();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NexoDbContext).Assembly);

        ApplyUserOwnedFilters(modelBuilder);

        if (Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            // SQLite has no decimal type; without this, SUM and ORDER BY on money
            // columns operate on text. Only the test host uses SQLite.
            foreach (var property in modelBuilder.Model.GetEntityTypes()
                         .SelectMany(t => t.GetProperties())
                         .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            {
                property.SetProviderClrType(typeof(double));
            }
        }

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Every entity implementing <see cref="IUserOwned"/> gets the same filter, so a
    /// new module cannot forget it. Adding an entity to the model is enough.
    /// </summary>
    private void ApplyUserOwnedFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(IUserOwned).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var userIdProperty = Expression.Property(parameter, nameof(IUserOwned.UserId));

            var contextConstant = Expression.Constant(this);
            var currentUserId = Expression.Property(contextConstant, nameof(CurrentUserId));

            var noAmbientUser = Expression.Equal(
                currentUserId,
                Expression.Constant(null, typeof(Guid?)));

            var matchesUser = Expression.Equal(
                Expression.Convert(userIdProperty, typeof(Guid?)),
                currentUserId);

            var body = Expression.OrElse(noAmbientUser, matchesUser);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, parameter));
        }
    }
}
