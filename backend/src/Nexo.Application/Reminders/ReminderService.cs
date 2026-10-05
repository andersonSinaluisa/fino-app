using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Application.Abstractions;
using Nexo.Application.Budgets;
using Nexo.Application.CreditCards;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Application.Notifications;
using Nexo.Domain.Accounts;
using Nexo.Domain.CreditCards;
using Nexo.Domain.Notifications;
using Nexo.Domain.Providers;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Reminders;

public interface IReminderService
{
    /// <summary>Evaluates and sends this person's due reminders. Returns how many were sent.</summary>
    Task<int> RunAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// Re-engagement reminders (pago de tarjeta, estado nuevo, presupuesto, cuenta
/// desactualizada, resumen semanal). The rules live in <see cref="ReminderRules"/>;
/// this class loads their inputs and enforces the delivery policy:
/// <list type="bullet">
/// <item>each reminder is sent at most once ever (Notification.DedupKey, unique per user);</item>
/// <item>at most one action reminder per local day, the most important one;</item>
/// <item>"vuelve a Fino" reminders are skipped if the person already opened the app today;</item>
/// <item>quiet hours, the Recordatorios switch and hidden amounts are applied by
/// <see cref="INotificationDispatcher"/>, exactly like every other notification.</item>
/// </list>
/// </summary>
public sealed class ReminderService(
    INexoDbContext db,
    CreditCardLedger cards,
    IBudgetService budgets,
    INotificationDispatcher dispatcher,
    ILogger<ReminderService> logger) : IReminderService
{
    private static readonly NotificationType[] CappedTypes =
    [
        NotificationType.CardPaymentDue,
        NotificationType.StatementAvailable,
        NotificationType.BudgetThreshold,
        NotificationType.AccountNeedsUpdate,
    ];

    public async Task<int> RunAsync(Guid userId, CancellationToken cancellationToken)
    {
        var context = await cards.OpenAsync(userId, cancellationToken);
        var dates = context.Dates;
        var localNow = dates.ToLocal(context.Now);
        var today = context.Today;
        var midnight = dates.StartOfDay(today.ToDateTime(TimeOnly.MinValue));

        // Outside every sending window there is nothing to evaluate: skip the queries.
        var inDayWindow = localNow.Hour is >= ReminderRules.DayStartHour and < ReminderRules.DayEndHour;
        var inWeeklyWindow = today.DayOfWeek == DayOfWeek.Sunday
                             && localNow.Hour is >= ReminderRules.WeeklyStartHour and < ReminderRules.WeeklyEndHour;
        if (!inDayWindow && !inWeeklyWindow)
        {
            return 0;
        }

        var inputs = new ReminderInputs(
            localNow,
            inDayWindow ? await LoadCardsAsync(context, cancellationToken) : [],
            inDayWindow ? await LoadBudgetsAsync(userId, cancellationToken) : [],
            inDayWindow ? await LoadImportedAccountsAsync(userId, dates, cancellationToken) : [],
            inWeeklyWindow ? await LoadWeeklyAsync(userId, dates, today, context.Now, cancellationToken) : null);

        var candidates = ReminderRules.Evaluate(inputs);
        if (candidates.Count == 0)
        {
            return 0;
        }

        var keys = candidates
            .SelectMany(c => (c.BlockedBy ?? []).Append(c.DedupKey))
            .Distinct()
            .ToList();
        var alreadySent = (await db.Notifications
                .AsNoTracking()
                .Where(n => n.UserId == userId && n.DedupKey != null && keys.Contains(n.DedupKey))
                .Select(n => n.DedupKey!)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var pending = candidates
            .Where(c => !alreadySent.Contains(c.DedupKey) && !(c.BlockedBy ?? []).Any(alreadySent.Contains))
            .ToList();
        if (pending.Count == 0)
        {
            return 0;
        }

        var toSend = new List<ReminderCandidate>();

        var capped = pending.Where(c => c.CountsTowardDailyCap).ToList();
        if (capped.Count > 0)
        {
            var sentToday = await db.Notifications
                .AsNoTracking()
                .AnyAsync(
                    n => n.UserId == userId
                         && n.DedupKey != null
                         && CappedTypes.Contains(n.Type)
                         && n.CreatedAt >= midnight,
                    cancellationToken);

            if (!sentToday)
            {
                var openedToday = await OpenedAppSinceAsync(userId, midnight, cancellationToken);
                var best = capped.FirstOrDefault(c => !c.RequiresInactivity || !openedToday);
                if (best is not null)
                {
                    toSend.Add(best);
                }
            }
        }

        toSend.AddRange(pending.Where(c => !c.CountsTowardDailyCap));

        var sent = 0;
        foreach (var reminder in toSend)
        {
            try
            {
                await dispatcher.DispatchAsync(
                    Notification.Create(
                        userId,
                        reminder.Type,
                        reminder.Title,
                        reminder.Body,
                        context.Now,
                        NotificationPayload.Build(reminder.Data.Select(d => (d.Key, d.Value)).ToArray()),
                        reminder.DedupKey),
                    cancellationToken);
                sent++;
            }
            catch (DbUpdateException ex)
            {
                // Another pass saved the same DedupKey first: the unique index did its job.
                logger.LogInformation(ex, "Reminder {Key} was already sent for user {UserId}.", reminder.DedupKey, userId);
                break;
            }
        }

        return sent;
    }

    /// <summary>Any app launch (device check-in) or session refresh since local midnight.</summary>
    private async Task<bool> OpenedAppSinceAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken) =>
        await db.Devices.AsNoTracking().AnyAsync(d => d.UserId == userId && d.LastSeenAt >= since, cancellationToken)
        || await db.RefreshTokens.AsNoTracking().AnyAsync(t => t.UserId == userId && t.CreatedAt >= since, cancellationToken);

    private async Task<IReadOnlyList<CardReminderInput>> LoadCardsAsync(CreditCardLedger.Context context, CancellationToken cancellationToken)
    {
        var states = await cards.LoadAsync(context, includeArchived: false, cancellationToken);
        return states
            .Where(s => s.Snapshot is not null)
            .Select(s =>
            {
                var next = s.Snapshot!.NextPayment;
                var last = s.Snapshot.LastStatement;
                return new CardReminderInput(
                    s.Account.Id,
                    s.Account.Alias,
                    next?.Amount,
                    next?.DueDate,
                    next?.FromClosedStatement ?? false,
                    next?.IsOverdue ?? false,
                    last?.ClosingDate,
                    last?.IsDeclared ?? false,
                    last is null || last.Status == StatementStatus.Open);
            })
            .ToList();
    }

    private async Task<IReadOnlyList<BudgetReminderInput>> LoadBudgetsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var overview = await budgets.ListAsync(userId, null, cancellationToken);
        return overview.Budgets
            .Where(b => b is { IsActive: true, Window: not null, Progress: not null })
            .Select(b => new BudgetReminderInput(b.Id, b.Name, b.Window!.Start, b.Progress!.Amount, b.Progress.Spent, b.Progress.PercentUsed))
            .ToList();
    }

    private async Task<IReadOnlyList<AccountReminderInput>> LoadImportedAccountsAsync(
        Guid userId,
        StatementDateInterpreter dates,
        CancellationToken cancellationToken)
    {
        var accounts = await db.FinancialAccounts
            .AsNoTracking()
            .Where(a => a.UserId == userId
                        && !a.IsArchived
                        && a.ConnectionMode == ConnectionMode.ManualImport
                        && a.AccountType != AccountKinds.LiabilityType)
            .Select(a => new { a.Id, a.Alias, a.LastSyncedAt, a.CreatedAt })
            .ToListAsync(cancellationToken);

        return accounts
            .Select(a => new AccountReminderInput(a.Id, a.Alias, DateOnly.FromDateTime(dates.ToLocalDate(a.LastSyncedAt ?? a.CreatedAt))))
            .ToList();
    }

    private async Task<WeeklyInput> LoadWeeklyAsync(
        Guid userId,
        StatementDateInterpreter dates,
        DateOnly today,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var weekStart = ReminderRules.WeekStart(today);
        var from = dates.StartOfDay(weekStart.ToDateTime(TimeOnly.MinValue));
        var previousFrom = dates.StartOfDay(weekStart.AddDays(-7).ToDateTime(TimeOnly.MinValue));

        IQueryable<Transaction> Scoped(DateTimeOffset start, DateTimeOffset end) => db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.TransactionDate >= start
                        && t.TransactionDate < end
                        && (t.Status == TransactionStatus.Posted || t.Status == TransactionStatus.Pending));

        var thisWeek = Scoped(from, now.AddSeconds(1));
        var movements = await MoneyFlowsCountAsync(thisWeek, cancellationToken);
        var (_, expense) = await Transactions.MoneyFlows.SumAsync(thisWeek, cancellationToken);
        var (_, previous) = await Transactions.MoneyFlows.SumAsync(Scoped(previousFrom, from), cancellationToken);

        return new WeeklyInput(weekStart, movements, expense, previous);
    }

    private static Task<int> MoneyFlowsCountAsync(IQueryable<Transaction> scoped, CancellationToken cancellationToken) =>
        Transactions.MoneyFlows.Countable(scoped).CountAsync(cancellationToken);
}
