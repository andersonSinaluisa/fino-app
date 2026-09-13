using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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
using Nexo.Domain.Pulses;
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

    public DbSet<ImportColumnMapping> ImportColumnMappings => Set<ImportColumnMapping>();

    public DbSet<EmailConnection> EmailConnections => Set<EmailConnection>();

    public DbSet<TrustedSender> TrustedSenders => Set<TrustedSender>();

    public DbSet<Insight> Insights => Set<Insight>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<FinancialPulse> Pulses => Set<FinancialPulse>();

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

            var dateTimeOffsetConverter = new ValueConverter<DateTimeOffset, long>(
                value => value.ToUniversalTime().Ticks,
                value => new DateTimeOffset(value, TimeSpan.Zero));

            var nullableDateTimeOffsetConverter = new ValueConverter<DateTimeOffset?, long?>(
                value => value.HasValue ? value.Value.ToUniversalTime().Ticks : null,
                value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);

            // SQLite has no DateTimeOffset type. Mapping it as UTC ticks keeps date
            // filters and ordering translatable in the integration-test provider.
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
            {
                if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(dateTimeOffsetConverter);
                    property.SetProviderClrType(typeof(long));
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(nullableDateTimeOffsetConverter);
                    property.SetProviderClrType(typeof(long?));
                }
            }
        }

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Every entity implementing <see cref="IUserOwned"/> gets the same filter, so a
    /// new module cannot forget it. Adding an entity to the model is enough.
    /// </summary>
    /// <remarks>
    /// The comparison is built as <c>e.UserId == CurrentUserId.GetValueOrDefault()</c>,
    /// not <c>(Guid?)e.UserId == CurrentUserId</c>: widening the mapped, non-nullable
    /// <c>UserId</c> column to <see cref="Nullable{Guid}"/> with <c>Convert</c> produced
    /// a filter that, composed with almost any additional caller-side <c>Where</c> (a
    /// date range, a second equality -- nothing exotic), the SQLite provider reported
    /// as "could not be translated". Real example that surfaced this: every statement
    /// upload 500'd inside <c>DeduplicationService.CheckBatchAsync</c>, and
    /// <c>AuthService.ListSessionsAsync</c>'s <c>db.RefreshTokens.Where(r => r.UserId
    /// == userId &amp;&amp; r.RevokedAt == null &amp;&amp; r.ExpiresAt > now)</c> hit the
    /// exact same error with no <c>Contains</c>/OR-chain involved at all -- so this was
    /// never about <see cref="Nexo.Application.Common.QueryableGuidExtensions.WhereIdIn{T}"/>
    /// specifically, it was the query filter's own <c>Convert</c> node. Moving the
    /// nullability handling onto <c>CurrentUserId</c> (already <see cref="Nullable{Guid}"/>)
    /// via <c>GetValueOrDefault()</c> keeps every filtered query a plain <c>Guid == Guid</c>
    /// equality against the mapped column -- the one shape this codebase has confirmed
    /// both providers translate without issue, filter or no filter, with or without
    /// whatever the caller ANDs on top. When there is no ambient user, <c>noAmbientUser</c>
    /// is true and the OR passes every row regardless of what <c>matchesUser</c> evaluates
    /// to (it becomes <c>e.UserId == Guid.Empty</c>, which no real row matches anyway, but
    /// it is never what makes the filter pass in that case).
    /// </remarks>
    private void ApplyUserOwnedFilters(ModelBuilder modelBuilder)
    {
        var getValueOrDefault = typeof(Guid?).GetMethod(
            nameof(Nullable<Guid>.GetValueOrDefault),
            Type.EmptyTypes)!;

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

            var currentUserIdOrEmpty = Expression.Call(currentUserId, getValueOrDefault);
            var matchesUser = Expression.Equal(userIdProperty, currentUserIdOrEmpty);

            var body = Expression.OrElse(noAmbientUser, matchesUser);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, parameter));
        }
    }
}
