using System.Globalization;
using Nexo.Domain.Notifications;

namespace Nexo.Application.Reminders;

/// <summary>One reminder that could be sent now, before dedup, inactivity and the daily cap.</summary>
/// <param name="Priority">Lower wins when only one reminder fits in the day.</param>
/// <param name="DedupKey">Unique per user forever: the same key is never sent twice.</param>
/// <param name="RequiresInactivity">Only sent when the person has not opened the app today ("vuelve a Fino" reminders).</param>
/// <param name="CountsTowardDailyCap">False only for the weekly summary, which has its own fixed slot.</param>
/// <param name="BlockedBy">Other keys that, if already sent, make this one pointless (80% after 100%).</param>
public sealed record ReminderCandidate(
    NotificationType Type,
    int Priority,
    string Title,
    string Body,
    string DedupKey,
    IReadOnlyDictionary<string, string> Data,
    bool RequiresInactivity = false,
    bool CountsTowardDailyCap = true,
    IReadOnlyList<string>? BlockedBy = null);

public sealed record CardReminderInput(
    Guid AccountId,
    string Name,
    decimal? NextPaymentAmount,
    DateOnly? NextPaymentDue,
    bool NextPaymentFromClosedStatement,
    bool NextPaymentOverdue,
    DateOnly? LastClosingDate,
    bool LastStatementDeclared,
    bool LastStatementOpen);

public sealed record BudgetReminderInput(Guid BudgetId, string Name, DateOnly WindowStart, decimal Amount, decimal Spent, decimal PercentUsed);

public sealed record AccountReminderInput(Guid AccountId, string Name, DateOnly LastUpdated);

public sealed record WeeklyInput(DateOnly WeekStart, int Movements, decimal Expense, decimal PreviousExpense);

/// <summary>Everything the rules need about one person, already loaded. Dates are local (Ecuador).</summary>
public sealed record ReminderInputs(
    DateTimeOffset LocalNow,
    IReadOnlyList<CardReminderInput> Cards,
    IReadOnlyList<BudgetReminderInput> Budgets,
    IReadOnlyList<AccountReminderInput> ImportedAccounts,
    WeeklyInput? Weekly);

/// <summary>
/// Re-engagement reminders, as pure functions so every threshold is unit-tested
/// without a database. Wording stays short and never invents numbers: every
/// figure comes from the same ledgers the screens use. When a device hides
/// amounts, the dispatcher replaces the whole body anyway.
/// </summary>
public static class ReminderRules
{
    /// <summary>Action reminders go out between 10:00 and 19:59 local time.</summary>
    public const int DayStartHour = 10;

    public const int DayEndHour = 20;

    /// <summary>The weekly summary: Sunday from 19:00 to 21:59.</summary>
    public const int WeeklyStartHour = 19;

    public const int WeeklyEndHour = 22;

    /// <summary>"Cuenta desactualizada" after this many days, at most once per this many days per account.</summary>
    public const int StaleDays = 14;

    /// <summary>First day a card's "pago próximo" reminder can go out.</summary>
    public const int CardDueSoonDays = 3;

    private static readonly CultureInfo Money = CultureInfo.GetCultureInfo("en-US");

    private static readonly string[] ShortMonths = ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];

    /// <param name="ignoreSchedule">Manual test run: evaluate every rule whatever the hour or weekday.</param>
    public static IReadOnlyList<ReminderCandidate> Evaluate(ReminderInputs inputs, bool ignoreSchedule = false)
    {
        var list = new List<ReminderCandidate>();
        var hour = inputs.LocalNow.Hour;
        var today = DateOnly.FromDateTime(inputs.LocalNow.DateTime);

        if (ignoreSchedule || hour is >= DayStartHour and < DayEndHour)
        {
            foreach (var card in inputs.Cards)
            {
                if (CardDue(card, today) is { } due)
                {
                    list.Add(due);
                }

                if (Statement(card, today) is { } statement)
                {
                    list.Add(statement);
                }
            }

            foreach (var budget in inputs.Budgets)
            {
                if (Budget(budget) is { } reminder)
                {
                    list.Add(reminder);
                }
            }

            foreach (var account in inputs.ImportedAccounts)
            {
                if (Stale(account, today) is { } reminder)
                {
                    list.Add(reminder);
                }
            }
        }

        if ((ignoreSchedule || (today.DayOfWeek == DayOfWeek.Sunday && hour is >= WeeklyStartHour and < WeeklyEndHour))
            && inputs.Weekly is { } weekly
            && Weekly(weekly) is { } summary)
        {
            list.Add(summary);
        }

        return list.OrderBy(c => c.Priority).ToList();
    }

    public static ReminderCandidate? CardDue(CardReminderInput card, DateOnly today)
    {
        if (card is not { NextPaymentFromClosedStatement: true, NextPaymentOverdue: false, NextPaymentAmount: > 0m, NextPaymentDue: { } due })
        {
            return null;
        }

        var days = due.DayNumber - today.DayNumber;
        var amount = Format(card.NextPaymentAmount.Value);
        var data = Data(("cardId", card.AccountId.ToString()));

        if (days == 0)
        {
            return new ReminderCandidate(
                NotificationType.CardPaymentDue,
                1,
                "Tu tarjeta vence hoy",
                $"Hoy es el último día para pagar tu {card.Name}: {amount}.",
                $"card-due:{card.AccountId:N}:{due:yyyy-MM-dd}:today",
                data);
        }

        if (days is > 0 and <= CardDueSoonDays)
        {
            return new ReminderCandidate(
                NotificationType.CardPaymentDue,
                1,
                "Pago de tarjeta próximo",
                $"Tu {card.Name} vence el {ShortDate(due)}: {amount}.",
                $"card-due:{card.AccountId:N}:{due:yyyy-MM-dd}:soon",
                data);
        }

        return null;
    }

    /// <summary>2–5 days after a card's corte the bank has published the statement; only if it was not entered yet.</summary>
    public static ReminderCandidate? Statement(CardReminderInput card, DateOnly today)
    {
        if (card is not { LastClosingDate: { } closing, LastStatementDeclared: false, LastStatementOpen: false })
        {
            return null;
        }

        var days = today.DayNumber - closing.DayNumber;
        if (days is < 2 or > 5)
        {
            return null;
        }

        return new ReminderCandidate(
            NotificationType.StatementAvailable,
            2,
            "Estado de cuenta nuevo",
            $"Ya salió el estado de tu {card.Name} (corte del {ShortDate(closing)}). Revísalo en Fino.",
            $"statement:{card.AccountId:N}:{closing:yyyy-MM-dd}",
            Data(("cardId", card.AccountId.ToString())),
            RequiresInactivity: true);
    }

    public static ReminderCandidate? Budget(BudgetReminderInput budget)
    {
        if (budget.Amount <= 0m)
        {
            return null;
        }

        var start = budget.WindowStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var full = $"budget:{budget.BudgetId:N}:{start}:100";
        var data = Data(("budgetId", budget.BudgetId.ToString()));

        if (budget.PercentUsed >= 100m)
        {
            return new ReminderCandidate(
                NotificationType.BudgetThreshold,
                3,
                "Presupuesto superado",
                $"{budget.Name} llegó a {Format(budget.Spent)} de {Format(budget.Amount)} este período.",
                full,
                data);
        }

        if (budget.PercentUsed >= 80m)
        {
            return new ReminderCandidate(
                NotificationType.BudgetThreshold,
                3,
                "Presupuesto al límite",
                $"{budget.Name} va en {Math.Floor(budget.PercentUsed):0}% de su presupuesto.",
                $"budget:{budget.BudgetId:N}:{start}:80",
                data,
                BlockedBy: [full]);
        }

        return null;
    }

    public static ReminderCandidate? Stale(AccountReminderInput account, DateOnly today)
    {
        var days = today.DayNumber - account.LastUpdated.DayNumber;
        if (days < StaleDays)
        {
            return null;
        }

        var weeks = days / 7;
        var since = weeks >= 8 ? "hace más de un mes" : $"hace {weeks} semanas";

        // One key per 14-day step since the last update: at most one nudge every two
        // weeks, and a fresh cycle as soon as the account is updated again.
        return new ReminderCandidate(
            NotificationType.AccountNeedsUpdate,
            4,
            "Cuenta desactualizada",
            $"{account.Name} no se actualiza {since}. Importa tu último estado para ver tu dinero al día.",
            $"stale:{account.AccountId:N}:{account.LastUpdated:yyyyMMdd}:{days / StaleDays}",
            Data(("accountId", account.AccountId.ToString())),
            RequiresInactivity: true);
    }

    public static ReminderCandidate? Weekly(WeeklyInput weekly)
    {
        if (weekly.Movements == 0)
        {
            return null;
        }

        var comparison = weekly.PreviousExpense <= 0m
            ? "."
            : weekly.Expense < weekly.PreviousExpense
                ? ", menos que la semana anterior."
                : weekly.Expense > weekly.PreviousExpense
                    ? ", más que la semana anterior."
                    : ", igual que la semana anterior.";

        return new ReminderCandidate(
            NotificationType.WeeklySummary,
            9,
            "Tu resumen semanal",
            $"Esta semana gastaste {Format(weekly.Expense)}{comparison}",
            $"weekly:{weekly.WeekStart:yyyy-MM-dd}",
            Data(("screen", "estadisticas")),
            CountsTowardDailyCap: false);
    }

    /// <summary>The Monday that starts the week containing <paramref name="day"/>.</summary>
    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    private static string ShortDate(DateOnly date) => $"{date.Day} {ShortMonths[date.Month - 1]}";

    private static string Format(decimal amount) => "$" + Math.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("#,##0.00", Money);

    private static IReadOnlyDictionary<string, string> Data(params (string Key, string Value)[] entries) =>
        entries.ToDictionary(e => e.Key, e => e.Value);
}
