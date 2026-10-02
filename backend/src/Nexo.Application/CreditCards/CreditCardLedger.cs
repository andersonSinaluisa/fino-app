using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Domain.Accounts;
using Nexo.Domain.Common;
using Nexo.Domain.CreditCards;
using Nexo.Domain.Providers;
using Nexo.Domain.Transactions;
using Nexo.Domain.Users;

namespace Nexo.Application.CreditCards;

/// <summary>Everything known about one card account, already evaluated.</summary>
/// <param name="Card">Null for a card account created before this module (no limit/corte/pago yet).</param>
/// <param name="Snapshot">Null exactly when <paramref name="Card"/> is: no terms, no cycles.</param>
public sealed record CardState(
    FinancialAccount Account,
    CreditCard? Card,
    CreditCardSnapshot? Snapshot,
    IReadOnlyList<CardMovement> Movements,
    IReadOnlyList<InstallmentPlan> Plans,
    IReadOnlyList<CreditCardStatement> Declared)
{
    /// <summary>Debt shown even without terms: max(−balance, 0).</summary>
    public decimal CurrentDebt => Snapshot?.CurrentDebt ?? Math.Max(MoneyMath.Round(-Account.EstimatedBalance), 0m);
}

/// <summary>
/// Loads and evaluates a user's cards in one pass. The ONLY producer of
/// <see cref="CreditCardSnapshot"/>s: the card screens (CreditCardService) and
/// Comprometido (CommittedMoneyService) both read their numbers from here, so they
/// can never disagree.
/// </summary>
public sealed class CreditCardLedger(INexoDbContext db, IClock clock)
{
    /// <summary>How far back movements are loaded: enough for the 24 statements the calculator rebuilds.</summary>
    public const int HistoryMonths = CreditCardCalculator.MaximumHistory + 2;

    public sealed record Context(User User, StatementDateInterpreter Dates, DateTimeOffset Now, DateOnly Today, IReadOnlyDictionary<string, Provider> Providers);

    public async Task<Context> OpenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                   ?? throw new NotFoundException("User", userId);
        var dates = new StatementDateInterpreter(user.TimeZoneId);
        var now = clock.UtcNow;
        var providers = await db.Providers.AsNoTracking().ToDictionaryAsync(p => p.Code, StringComparer.Ordinal, cancellationToken);
        return new Context(user, dates, now, DateOnly.FromDateTime(dates.ToLocalDate(now)), providers);
    }

    /// <summary>Every card account of the user (archived ones only when asked), evaluated.</summary>
    public async Task<IReadOnlyList<CardState>> LoadAsync(
        Context context,
        bool includeArchived,
        CancellationToken cancellationToken,
        Guid? accountId = null)
    {
        var userId = context.User.Id;
        var accountsQuery = db.FinancialAccounts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.AccountType == AccountKinds.LiabilityType);

        if (accountId is { } id)
        {
            accountsQuery = accountsQuery.Where(a => a.Id == id);
        }
        else if (!includeArchived)
        {
            accountsQuery = accountsQuery.Where(a => !a.IsArchived);
        }

        var accounts = await accountsQuery
            .OrderBy(a => a.DisplayOrder)
            .ThenBy(a => a.Alias)
            .ToListAsync(cancellationToken);

        if (accounts.Count == 0)
        {
            return [];
        }

        var accountIds = accounts.Select(a => a.Id).ToArray();

        var cards = await db.CreditCards
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .WhereIdIn(c => c.FinancialAccountId, accountIds)
            .ToListAsync(cancellationToken);
        var cardsByAccount = cards.ToDictionary(c => c.FinancialAccountId);
        var cardIds = cards.Select(c => c.Id).ToArray();

        var since = context.Dates.StartOfDay(context.Dates.ToLocalDate(context.Now).AddMonths(-HistoryMonths));
        var rows = await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.TransactionDate >= since
                        && (t.Status == TransactionStatus.Posted || t.Status == TransactionStatus.Pending))
            .WhereIdIn(t => t.FinancialAccountId, accountIds)
            .Select(t => new { t.Id, t.FinancialAccountId, t.TransactionDate, t.Direction, t.CardMovementType, t.Amount, t.Description })
            .ToListAsync(cancellationToken);

        var plans = cardIds.Length == 0
            ? []
            : await db.InstallmentPlans
                .AsNoTracking()
                .Include(p => p.Installments)
                .Where(p => p.UserId == userId)
                .WhereIdIn(p => p.CreditCardId, cardIds)
                .ToListAsync(cancellationToken);

        var declared = cardIds.Length == 0
            ? []
            : await db.CreditCardStatements
                .AsNoTracking()
                .Where(s => s.UserId == userId)
                .WhereIdIn(s => s.CreditCardId, cardIds)
                .ToListAsync(cancellationToken);

        // A plan defers debt only while its purchase still counts (not ignored/deleted).
        var purchaseIds = plans.Select(p => p.TransactionId).ToArray();
        var countingPurchases = purchaseIds.Length == 0
            ? new HashSet<Guid>()
            : (await db.Transactions
                .AsNoTracking()
                .Where(t => t.UserId == userId
                            && (t.Status == TransactionStatus.Posted || t.Status == TransactionStatus.Pending))
                .WhereIdIn(t => t.Id, purchaseIds)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken)).ToHashSet();

        var result = new List<CardState>(accounts.Count);
        foreach (var account in accounts)
        {
            var movements = rows
                .Where(r => r.FinancialAccountId == account.Id)
                .Select(r => new CardMovement(
                    r.Id,
                    DateOnly.FromDateTime(context.Dates.ToLocalDate(r.TransactionDate)),
                    r.Direction,
                    // A movement that reached a card before it was classified (e.g. a card
                    // account older than this module) gets the same default the importer uses.
                    r.CardMovementType ?? CreditCardMovementRules.Classify(r.Direction, r.Description),
                    r.Amount))
                .ToList();

            cardsByAccount.TryGetValue(account.Id, out var card);
            var cardPlans = card is null ? [] : plans.Where(p => p.CreditCardId == card.Id).ToList();
            var cardDeclared = card is null ? [] : declared.Where(s => s.CreditCardId == card.Id).ToList();

            CreditCardSnapshot? snapshot = null;
            if (card is not null)
            {
                snapshot = CreditCardCalculator.Calculate(
                    card.Terms,
                    account.EstimatedBalance,
                    context.Today,
                    movements,
                    cardPlans.Select(p => p.ToSchedule(countingPurchases.Contains(p.TransactionId))).ToList(),
                    cardDeclared.Select(s => s.ToDeclared()).ToList());
            }

            result.Add(new CardState(account, card, snapshot, movements, cardPlans, cardDeclared));
        }

        return result;
    }

    public async Task<CardState> RequireAsync(Context context, Guid accountId, CancellationToken cancellationToken)
    {
        var states = await LoadAsync(context, includeArchived: true, cancellationToken, accountId);
        return states.Count == 1 ? states[0] : throw new NotFoundException("CreditCard", accountId);
    }
}
