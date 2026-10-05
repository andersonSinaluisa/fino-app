using Nexo.Application.Reminders;
using Nexo.Domain.Notifications;
using Xunit;

namespace Nexo.Application.Tests;

/// <summary>
/// Recordatorios: when each reminder fires, its wording, and the keys that make
/// it impossible to send twice.
/// </summary>
public class ReminderRulesTests
{
    private static readonly Guid CardId = Guid.CreateVersion7();
    private static readonly DateOnly Today = new(2026, 11, 2); // Monday

    private static DateTimeOffset At(DateOnly day, int hour) =>
        new(day.ToDateTime(new TimeOnly(hour, 5)), TimeSpan.FromHours(-5));

    private static CardReminderInput Card(
        DateOnly? due = null,
        decimal amount = 247.03m,
        bool closed = true,
        bool overdue = false,
        DateOnly? closing = null,
        bool declared = false) =>
        new(CardId, "Visa Pichincha", amount, due, closed, overdue, closing, declared, LastStatementOpen: closing is null);

    private static ReminderInputs Inputs(DateTimeOffset now, params CardReminderInput[] cards) => new(now, cards, [], [], null);

    [Fact]
    public void Card_payment_reminder_goes_out_three_days_before_and_on_the_due_date()
    {
        var soon = ReminderRules.CardDue(Card(due: new DateOnly(2026, 11, 5)), Today);
        Assert.NotNull(soon);
        Assert.Equal(NotificationType.CardPaymentDue, soon.Type);
        Assert.Equal("Tu Visa Pichincha vence el 5 nov: $247.03.", soon.Body);
        Assert.EndsWith(":soon", soon.DedupKey);
        Assert.Equal(CardId.ToString(), soon.Data["cardId"]);

        // Missed the third day (e.g. capped): the same "soon" reminder still goes out the next days.
        var later = ReminderRules.CardDue(Card(due: new DateOnly(2026, 11, 5)), Today.AddDays(2));
        Assert.Equal(soon.DedupKey, later!.DedupKey);

        var today = ReminderRules.CardDue(Card(due: Today), Today);
        Assert.Equal("Tu tarjeta vence hoy", today!.Title);
        Assert.EndsWith(":today", today.DedupKey);

        Assert.Null(ReminderRules.CardDue(Card(due: Today.AddDays(4)), Today));
        Assert.Null(ReminderRules.CardDue(Card(due: Today.AddDays(-1)), Today));
    }

    [Fact]
    public void No_card_reminder_for_estimates_overdue_or_nothing_to_pay()
    {
        var due = Today.AddDays(1);
        Assert.Null(ReminderRules.CardDue(Card(due: due, closed: false), Today));
        Assert.Null(ReminderRules.CardDue(Card(due: due, overdue: true), Today));
        Assert.Null(ReminderRules.CardDue(Card(due: due, amount: 0m), Today));
    }

    [Fact]
    public void New_statement_reminder_is_for_two_to_five_days_after_the_closing_and_only_if_not_entered()
    {
        Assert.Null(ReminderRules.Statement(Card(closing: Today.AddDays(-1)), Today));
        var reminder = ReminderRules.Statement(Card(closing: Today.AddDays(-2)), Today);
        Assert.NotNull(reminder);
        Assert.True(reminder.RequiresInactivity);
        Assert.Contains("corte del 31 oct", reminder.Body);
        Assert.NotNull(ReminderRules.Statement(Card(closing: Today.AddDays(-5)), Today));
        Assert.Null(ReminderRules.Statement(Card(closing: Today.AddDays(-6)), Today));
        Assert.Null(ReminderRules.Statement(Card(closing: Today.AddDays(-3), declared: true), Today));
    }

    [Fact]
    public void Budget_warns_at_80_and_100_percent_and_80_is_blocked_once_100_was_sent()
    {
        var id = Guid.CreateVersion7();
        var start = new DateOnly(2026, 11, 1);

        Assert.Null(ReminderRules.Budget(new BudgetReminderInput(id, "Comida", start, 300m, 230m, 76.6m)));

        var eighty = ReminderRules.Budget(new BudgetReminderInput(id, "Comida", start, 300m, 246m, 82m));
        Assert.Equal("Comida va en 82% de su presupuesto.", eighty!.Body);
        Assert.EndsWith(":80", eighty.DedupKey);

        var full = ReminderRules.Budget(new BudgetReminderInput(id, "Comida", start, 300m, 1312.5m, 437.5m));
        Assert.Equal("Presupuesto superado", full!.Title);
        Assert.Equal("Comida llegó a $1,312.50 de $300.00 este período.", full.Body);
        Assert.Contains(full.DedupKey, eighty.BlockedBy!);

        Assert.Null(ReminderRules.Budget(new BudgetReminderInput(id, "Sin monto", start, 0m, 10m, 0m)));
    }

    [Fact]
    public void Stale_account_reminder_after_14_days_at_most_every_14_days_and_resets_after_an_update()
    {
        var id = Guid.CreateVersion7();
        Assert.Null(ReminderRules.Stale(new AccountReminderInput(id, "Banco Pichincha", Today.AddDays(-13)), Today));

        var first = ReminderRules.Stale(new AccountReminderInput(id, "Banco Pichincha", Today.AddDays(-14)), Today);
        Assert.Equal("Banco Pichincha no se actualiza hace 2 semanas. Importa tu último estado para ver tu dinero al día.", first!.Body);
        var sameStep = ReminderRules.Stale(new AccountReminderInput(id, "Banco Pichincha", Today.AddDays(-14)), Today.AddDays(13));
        Assert.Equal(first.DedupKey, sameStep!.DedupKey);
        var nextStep = ReminderRules.Stale(new AccountReminderInput(id, "Banco Pichincha", Today.AddDays(-14)), Today.AddDays(14));
        Assert.NotEqual(first.DedupKey, nextStep!.DedupKey);

        var afterUpdate = ReminderRules.Stale(new AccountReminderInput(id, "Banco Pichincha", Today), Today.AddDays(14));
        Assert.NotEqual(first.DedupKey, afterUpdate!.DedupKey);
        Assert.Equal(id.ToString(), afterUpdate.Data["accountId"]);
    }

    [Fact]
    public void Weekly_summary_compares_with_the_previous_week_and_skips_empty_weeks()
    {
        var monday = new DateOnly(2026, 11, 2);
        Assert.Null(ReminderRules.Weekly(new WeeklyInput(monday, 0, 0m, 120m)));
        Assert.Equal("Esta semana gastaste $214.00, menos que la semana anterior.", ReminderRules.Weekly(new WeeklyInput(monday, 9, 214m, 260m))!.Body);
        Assert.Equal("Esta semana gastaste $214.00, más que la semana anterior.", ReminderRules.Weekly(new WeeklyInput(monday, 9, 214m, 100m))!.Body);
        Assert.Equal("Esta semana gastaste $214.00.", ReminderRules.Weekly(new WeeklyInput(monday, 9, 214m, 0m))!.Body);
        Assert.False(ReminderRules.Weekly(new WeeklyInput(monday, 1, 5m, 0m))!.CountsTowardDailyCap);
        Assert.Equal(monday, ReminderRules.WeekStart(new DateOnly(2026, 11, 8)));
        Assert.Equal(monday, ReminderRules.WeekStart(monday));
    }

    [Fact]
    public void Nothing_goes_out_at_night_and_the_weekly_summary_only_on_sunday_evening()
    {
        var card = Card(due: Today);
        Assert.Empty(ReminderRules.Evaluate(Inputs(At(Today, 7), card)));
        Assert.Empty(ReminderRules.Evaluate(Inputs(At(Today, 21), card)));
        Assert.Single(ReminderRules.Evaluate(Inputs(At(Today, 10), card)));

        var sunday = new DateOnly(2026, 11, 8);
        var weekly = new WeeklyInput(ReminderRules.WeekStart(sunday), 4, 50m, 60m);
        Assert.Empty(ReminderRules.Evaluate(new ReminderInputs(At(sunday, 18), [], [], [], weekly)).Where(c => c.Type == NotificationType.WeeklySummary));
        Assert.Single(ReminderRules.Evaluate(new ReminderInputs(At(sunday, 19), [], [], [], weekly)));
        Assert.Empty(ReminderRules.Evaluate(new ReminderInputs(At(Today, 19), [], [], [], weekly)));
    }

    [Fact]
    public void Candidates_come_ordered_by_importance()
    {
        var inputs = new ReminderInputs(
            At(Today, 11),
            [Card(due: Today.AddDays(2), closing: Today.AddDays(-3))],
            [new BudgetReminderInput(Guid.CreateVersion7(), "Comida", Today, 100m, 90m, 90m)],
            [new AccountReminderInput(Guid.CreateVersion7(), "Banco", Today.AddDays(-30))],
            null);

        var types = ReminderRules.Evaluate(inputs).Select(c => c.Type).ToList();
        Assert.Equal(
            [NotificationType.CardPaymentDue, NotificationType.StatementAvailable, NotificationType.BudgetThreshold, NotificationType.AccountNeedsUpdate],
            types);
    }
}
