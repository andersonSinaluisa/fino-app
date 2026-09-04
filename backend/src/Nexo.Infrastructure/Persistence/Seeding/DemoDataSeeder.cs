using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Application.Abstractions;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Domain.Accounts;
using Nexo.Domain.Categories;
using Nexo.Domain.Common;
using Nexo.Domain.Providers;
using Nexo.Domain.Transactions;
using Nexo.Domain.Users;

namespace Nexo.Infrastructure.Persistence.Seeding;

public sealed class DemoSeedOptions
{
    public const string SectionName = "Nexo:Seed";

    /// <summary>Never enable outside Development. Guarded again in the host.</summary>
    public bool Demo { get; set; }

    public string Email { get; set; } = "anderson@nexo.dev";

    public string Password { get; set; } = "NexoDemo2026!";

    public string DisplayName { get; set; } = "Anderson";
}

/// <summary>
/// Development-only fixture: one fictional user with four accounts and three months
/// of movements, shaped so every screen has something real to show — including the
/// month-over-month insight ("gastaste $120 menos que el mes pasado") and a
/// recurring subscription. Nothing here is real data from any institution.
/// </summary>
public sealed class DemoDataSeeder(
    NexoDbContext db,
    IPasswordHasher passwordHasher,
    IClock clock,
    ILogger<DemoDataSeeder> logger)
{
    private sealed record Template(
        int Day,
        string ProviderCode,
        string Description,
        decimal Amount,
        TransactionDirection Direction,
        string CategoryCode,
        string? Reference = null);

    private const decimal PichinchaTarget = 1240.50m;
    private const decimal GuayaquilTarget = 860.20m;
    private const decimal DeunaTarget = 485.50m;
    private const decimal PayPhoneTarget = 260.00m;

    public async Task SeedAsync(DemoSeedOptions options, CancellationToken cancellationToken = default)
    {
        var email = User.NormalizeEmail(options.Email);
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == email, cancellationToken))
        {
            logger.LogInformation("Demo user already exists; skipping demo seed.");
            return;
        }

        var now = clock.UtcNow;
        var dates = StatementDateInterpreter.Ecuador();

        var user = User.Register(email, options.DisplayName, passwordHasher.Hash(options.Password), now);
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        var providers = await db.Providers.ToDictionaryAsync(p => p.Code, cancellationToken);
        var categories = await db.Categories
            .Where(c => c.UserId == null)
            .ToDictionaryAsync(c => c.Code, c => c.Id, cancellationToken);

        var accounts = new Dictionary<string, FinancialAccount>(StringComparer.Ordinal)
        {
            [ProviderCodes.Pichincha] = FinancialAccount.Open(
                user.Id, providers[ProviderCodes.Pichincha], "Banco Pichincha", AccountType.Savings,
                ConnectionMode.ManualImport, now, "4821", Currency.Usd, null, 1),
            [ProviderCodes.Guayaquil] = FinancialAccount.Open(
                user.Id, providers[ProviderCodes.Guayaquil], "Banco Guayaquil", AccountType.Checking,
                ConnectionMode.ManualImport, now, "7345", Currency.Usd, null, 2),
            [ProviderCodes.Deuna] = FinancialAccount.Open(
                user.Id, providers[ProviderCodes.Deuna], "DEUNA", AccountType.Wallet,
                ConnectionMode.ManualImport, now, "0912", Currency.Usd, null, 3),
            [ProviderCodes.PayPhone] = FinancialAccount.Open(
                user.Id, providers[ProviderCodes.PayPhone], "PayPhone", AccountType.Wallet,
                ConnectionMode.ManualImport, now, "5560", Currency.Usd, null, 4),
        };

        db.FinancialAccounts.AddRange(accounts.Values);
        await db.SaveChangesAsync(cancellationToken);

        var localNow = dates.ToLocal(now);
        var months = new[]
        {
            new DateTime(localNow.Year, localNow.Month, 1).AddMonths(-2),
            new DateTime(localNow.Year, localNow.Month, 1).AddMonths(-1),
            new DateTime(localNow.Year, localNow.Month, 1),
        };

        var transactions = new List<Transaction>();

        for (var monthIndex = 0; monthIndex < months.Length; monthIndex++)
        {
            var month = months[monthIndex];
            var extra = monthIndex switch
            {
                0 => 60.00m,   // two months ago: $920 spent
                1 => 120.00m,  // last month:     $980 spent
                _ => 0m,       // this month:     $860 spent  -> "$120 menos que el mes pasado"
            };

            foreach (var template in BuildMonth(extra))
            {
                var day = Math.Min(template.Day, DateTime.DaysInMonth(month.Year, month.Month));
                var localDate = new DateTime(month.Year, month.Month, day);
                var instant = dates.ToInstant(localDate, TimeSpan.FromHours(10 + (template.Day % 8)));

                if (instant > now)
                {
                    continue;
                }

                var account = accounts[template.ProviderCode];
                categories.TryGetValue(template.CategoryCode, out var categoryId);

                transactions.Add(Transaction.Create(
                    user.Id,
                    account.Id,
                    account.ProviderCode,
                    instant,
                    template.Amount,
                    template.Direction,
                    template.Description,
                    TransactionSource.Import,
                    now,
                    account.Currency,
                    template.Reference,
                    categoryId: categoryId == Guid.Empty ? null : categoryId,
                    accountMask: account.Mask,
                    confidence: SourceConfidence.High));
            }
        }

        db.Transactions.AddRange(transactions);

        // Anchor each account so the running estimate lands exactly on the balance
        // the product mock-ups show, with every movement dated after the anchor —
        // which is precisely the "saldo estimado" case the UI must communicate.
        var targets = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [ProviderCodes.Pichincha] = PichinchaTarget,
            [ProviderCodes.Guayaquil] = GuayaquilTarget,
            [ProviderCodes.Deuna] = DeunaTarget,
            [ProviderCodes.PayPhone] = PayPhoneTarget,
        };

        foreach (var (providerCode, account) in accounts)
        {
            var accountMovements = transactions.Where(t => t.FinancialAccountId == account.Id).ToList();
            var net = accountMovements.Sum(t => t.SignedAmount);
            var earliest = accountMovements.Count > 0
                ? accountMovements.Min(t => t.TransactionDate)
                : now;

            account.SetVerifiedBalance(targets[providerCode] - net, earliest.AddDays(-1), now);

            foreach (var movement in accountMovements.OrderBy(t => t.TransactionDate))
            {
                account.ApplyMovement(movement.SignedAmount, movement.TransactionDate, now);
            }

            account.MarkSynced(now.AddMinutes(-5));
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Demo data seeded for {Email}: {Accounts} accounts, {Transactions} movements.",
            email,
            accounts.Count,
            transactions.Count);
    }

    /// <summary>
    /// One month of movements. Expenses total $860.00 plus <paramref name="extraExpense"/>,
    /// income is a single $1,420.00 salary — matching the numbers on the home screen.
    /// </summary>
    private static IEnumerable<Template> BuildMonth(decimal extraExpense)
    {
        var items = new List<Template>
        {
            new(1, ProviderCodes.Pichincha, "Acreditación rol de pagos", 1420.00m, TransactionDirection.Income, CategoryCodes.Income, "ROL-0001"),
            new(2, ProviderCodes.Pichincha, "SUPERMAXI ALBORADA", 48.20m, TransactionDirection.Expense, CategoryCodes.Groceries),
            new(3, ProviderCodes.Deuna, "UBER TRIP", 6.80m, TransactionDirection.Expense, CategoryCodes.Transport),
            new(4, ProviderCodes.PayPhone, "KFC MALL DEL SOL", 18.75m, TransactionDirection.Expense, CategoryCodes.Food),
            new(5, ProviderCodes.Pichincha, "NETFLIX.COM", 12.99m, TransactionDirection.Expense, CategoryCodes.Subscriptions),
            new(6, ProviderCodes.Guayaquil, "PRIMAX VIA DAULE", 35.00m, TransactionDirection.Expense, CategoryCodes.Transport),
            new(7, ProviderCodes.Deuna, "UBER TRIP", 8.20m, TransactionDirection.Expense, CategoryCodes.Transport),
            new(8, ProviderCodes.Pichincha, "CLARO PLAN MOVIL", 28.90m, TransactionDirection.Expense, CategoryCodes.Utilities),
            new(9, ProviderCodes.Pichincha, "SUPERMAXI ALBORADA", 92.40m, TransactionDirection.Expense, CategoryCodes.Groceries),
            new(10, ProviderCodes.Pichincha, "EMPRESA ELECTRICA GUAYAQUIL", 22.35m, TransactionDirection.Expense, CategoryCodes.Utilities),
            new(10, ProviderCodes.Pichincha, "INTERAGUA", 14.60m, TransactionDirection.Expense, CategoryCodes.Utilities),
            new(11, ProviderCodes.Deuna, "UBER TRIP", 5.40m, TransactionDirection.Expense, CategoryCodes.Transport),
            new(12, ProviderCodes.Pichincha, "SPOTIFY AB", 5.99m, TransactionDirection.Expense, CategoryCodes.Subscriptions),
            new(13, ProviderCodes.Guayaquil, "FYBECA URDESA", 31.45m, TransactionDirection.Expense, CategoryCodes.Health),
            new(14, ProviderCodes.Deuna, "UBER TRIP", 7.60m, TransactionDirection.Expense, CategoryCodes.Transport),
            new(15, ProviderCodes.PayPhone, "RAPPI PEDIDO", 24.30m, TransactionDirection.Expense, CategoryCodes.Food),
            new(16, ProviderCodes.Pichincha, "SUPERMAXI ALBORADA", 63.15m, TransactionDirection.Expense, CategoryCodes.Groceries),
            new(17, ProviderCodes.PayPhone, "SWEET & COFFEE", 4.50m, TransactionDirection.Expense, CategoryCodes.Food),
            new(18, ProviderCodes.Guayaquil, "PRIMAX VIA DAULE", 40.00m, TransactionDirection.Expense, CategoryCodes.Transport),
            new(19, ProviderCodes.Guayaquil, "CINEMARK MALL DEL SOL", 22.00m, TransactionDirection.Expense, CategoryCodes.Entertainment),
            new(20, ProviderCodes.Guayaquil, "DE PRATI", 85.90m, TransactionDirection.Expense, CategoryCodes.Shopping),
            new(21, ProviderCodes.Pichincha, "MERCADO LIBRE EC", 47.99m, TransactionDirection.Expense, CategoryCodes.Shopping),
            new(22, ProviderCodes.PayPhone, "JUAN VALDEZ", 6.25m, TransactionDirection.Expense, CategoryCodes.Food),
            new(23, ProviderCodes.Pichincha, "TRANSFERENCIA ENVIADA", 120.00m, TransactionDirection.Expense, CategoryCodes.Transfers, "TRF-88210"),
            new(24, ProviderCodes.Pichincha, "AWS EMEA SERVICES", 18.44m, TransactionDirection.Expense, CategoryCodes.Subscriptions),
            new(25, ProviderCodes.Pichincha, "MEGAMAXI CEIBOS", 88.84m, TransactionDirection.Expense, CategoryCodes.Groceries),
        };

        if (extraExpense > 0m)
        {
            items.Add(new Template(
                26,
                ProviderCodes.Guayaquil,
                "ETAFASHION MALL DEL SUR",
                extraExpense,
                TransactionDirection.Expense,
                CategoryCodes.Shopping));
        }

        return items;
    }
}
