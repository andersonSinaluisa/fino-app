using Nexo.Domain.Accounts;
using Nexo.Domain.Budgets;
using Nexo.Domain.Common;
using Nexo.Domain.CreditCards;
using Nexo.Domain.Providers;
using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Domain.Tests;

/// <summary>
/// Tarjetas de crédito: billing calendar, the single calculator behind every card
/// figure, movement types and installment plans.
/// </summary>
public class BillingCalendarTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void A_purchase_before_the_closing_belongs_to_that_statement_and_one_after_to_the_next()
    {
        var tenth = BillingCalendar.CycleContaining(D(2026, 9, 10), closingDay: 15, dueDay: 30);
        var sixteenth = BillingCalendar.CycleContaining(D(2026, 9, 16), 15, 30);

        Assert.Equal(new BillingCycle(D(2026, 8, 16), D(2026, 9, 15), D(2026, 9, 30)), tenth);
        Assert.Equal(new BillingCycle(D(2026, 9, 16), D(2026, 10, 15), D(2026, 10, 30)), sixteenth);
    }

    [Fact]
    public void A_purchase_on_the_closing_date_belongs_to_the_statement_that_closes_that_day()
    {
        var cycle = BillingCalendar.CycleContaining(D(2026, 9, 15), 15, 30);
        Assert.Equal(D(2026, 9, 15), cycle.Closing);
        Assert.True(cycle.Contains(D(2026, 9, 15)));
    }

    [Fact]
    public void Corte_31_closes_on_the_last_day_of_february_in_normal_and_leap_years()
    {
        Assert.Equal(D(2027, 2, 28), BillingCalendar.ClosingOnOrAfter(D(2027, 2, 10), 31));
        Assert.Equal(D(2028, 2, 29), BillingCalendar.ClosingOnOrAfter(D(2028, 2, 10), 31));

        // Cycles stay contiguous across the short month: 1 feb – 29 feb, then 1 mar – 31 mar.
        var february = BillingCalendar.CycleContaining(D(2028, 2, 29), 31, 15);
        var march = BillingCalendar.CycleContaining(D(2028, 3, 1), 31, 15);
        Assert.Equal(D(2028, 2, 1), february.Start);
        Assert.Equal(D(2028, 3, 1), march.Start);
        Assert.Equal(D(2028, 3, 31), march.Closing);
    }

    [Fact]
    public void Pago_31_is_paid_on_the_last_day_of_short_months()
    {
        Assert.Equal(D(2026, 4, 30), BillingCalendar.DueDateFor(D(2026, 4, 15), 15, 31));
        Assert.Equal(D(2027, 2, 28), BillingCalendar.DueDateFor(D(2027, 2, 15), 15, 31));
        Assert.Equal(D(2028, 2, 29), BillingCalendar.DueDateFor(D(2028, 2, 15), 15, 31));
    }

    [Fact]
    public void A_payment_day_before_the_closing_day_falls_in_the_next_month_across_the_year_end()
    {
        var cycle = BillingCalendar.CycleContaining(D(2026, 12, 22), closingDay: 25, dueDay: 10);
        Assert.Equal(D(2026, 12, 25), cycle.Closing);
        Assert.Equal(D(2027, 1, 10), cycle.Due);

        var next = BillingCalendar.CycleContaining(D(2026, 12, 27), 25, 10);
        Assert.Equal(D(2026, 12, 26), next.Start);
        Assert.Equal(D(2027, 1, 25), next.Closing);
        Assert.Equal(D(2027, 2, 10), next.Due);
    }

    [Fact]
    public void When_a_short_month_clamps_both_days_together_payment_moves_to_the_day_after_the_closing()
    {
        // Corte 30, pago 31: in February both become the 28th.
        Assert.Equal(D(2027, 3, 1), BillingCalendar.DueDateFor(D(2027, 2, 28), 30, 31));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(32, 10)]
    [InlineData(15, 0)]
    [InlineData(15, 15)]
    public void Invalid_days_are_rejected(int closingDay, int dueDay) =>
        Assert.Throws<DomainException>(() => BillingCalendar.EnsureValidDays(closingDay, dueDay));
}

public class CreditCardCalculatorTests
{
    private static readonly CreditCardTerms Terms = new(2000m, ClosingDay: 15, PaymentDueDay: 30, AutoReserve: true);

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static CardMovement Purchase(DateOnly date, decimal amount) =>
        new(Guid.CreateVersion7(), date, TransactionDirection.Expense, CreditCardMovementType.Purchase, amount);

    private static CardMovement Payment(DateOnly date, decimal amount) =>
        new(Guid.CreateVersion7(), date, TransactionDirection.Income, CreditCardMovementType.Payment, amount);

    private static CardMovement Of(CreditCardMovementType type, TransactionDirection direction, DateOnly date, decimal amount) =>
        new(Guid.CreateVersion7(), date, direction, type, amount);

    private static decimal Balance(params CardMovement[] movements) => -movements.Sum(m => m.DebtDelta);

    private static CreditCardSnapshot Run(DateOnly today, CardMovement[] movements, InstallmentPlanSchedule[]? plans = null, DeclaredStatement[]? declared = null, CreditCardTerms? terms = null) =>
        CreditCardCalculator.Calculate(terms ?? Terms, Balance(movements), today, movements, plans ?? [], declared ?? []);

    [Fact]
    public void Debt_limit_available_credit_and_utilization()
    {
        var snapshot = Run(D(2026, 10, 5), [Purchase(D(2026, 10, 1), 750.32m)]);

        Assert.Equal(750.32m, snapshot.CurrentDebt);
        Assert.Equal(2000m, snapshot.CreditLimit);
        Assert.Equal(1249.68m, snapshot.AvailableCredit);
        Assert.Equal(37.5m, snapshot.UtilizationPercent);
        Assert.False(snapshot.IsOverLimit);
    }

    [Fact]
    public void A_purchase_in_the_open_cycle_is_the_next_payment_and_paying_it_clears_it()
    {
        var purchase = Purchase(D(2026, 3, 10), 100m);
        var before = Run(D(2026, 3, 10), [purchase]);

        Assert.Equal(100m, before.CurrentDebt);
        Assert.NotNull(before.NextPayment);
        Assert.Equal(100m, before.NextPayment!.Amount);
        Assert.Equal(D(2026, 3, 30), before.NextPayment.DueDate);
        Assert.False(before.NextPayment.FromClosedStatement);
        Assert.Equal(100m, before.CommittedContribution);

        var after = Run(D(2026, 3, 10), [purchase, Payment(D(2026, 3, 10), 100m)]);
        Assert.Equal(0m, after.CurrentDebt);
        Assert.Null(after.NextPayment);
        Assert.Equal(0m, after.CommittedContribution);
    }

    [Fact]
    public void Without_auto_reserve_the_debt_is_shown_but_nothing_is_committed()
    {
        var snapshot = Run(D(2026, 3, 10), [Purchase(D(2026, 3, 9), 100m)], terms: Terms with { AutoReserve = false });

        Assert.Equal(100m, snapshot.CurrentDebt);
        Assert.Equal(100m, snapshot.NextPayment!.Amount);
        Assert.Equal(0m, snapshot.CommittedContribution);
    }

    [Fact]
    public void A_partial_payment_leaves_the_rest_pending_and_paying_it_marks_the_statement_paid()
    {
        var purchase = Purchase(D(2026, 9, 10), 420m);
        var partial = Run(D(2026, 9, 25), [purchase, Payment(D(2026, 9, 20), 100m)]);

        var statement = partial.LastStatement!;
        Assert.Equal(D(2026, 9, 15), statement.ClosingDate);
        Assert.Equal(D(2026, 9, 30), statement.DueDate);
        Assert.Equal(420m, statement.StatementBalance);
        Assert.Equal(100m, statement.AmountPaid);
        Assert.Equal(320m, statement.Pending);
        Assert.Equal(StatementStatus.PartiallyPaid, statement.Status);
        Assert.Equal(320m, partial.NextPayment!.Amount);
        Assert.True(partial.NextPayment.FromClosedStatement);
        Assert.Equal(320m, partial.CommittedContribution);

        var paid = Run(D(2026, 9, 25), [purchase, Payment(D(2026, 9, 20), 100m), Payment(D(2026, 9, 24), 320m)]);
        Assert.Equal(StatementStatus.Paid, paid.LastStatement!.Status);
        Assert.Equal(0m, paid.LastStatement.Pending);
        Assert.Null(paid.NextPayment);
    }

    [Fact]
    public void A_closed_statement_with_nothing_paid_is_closed_and_after_its_due_date_overdue()
    {
        var purchase = Purchase(D(2026, 9, 10), 420m);

        Assert.Equal(StatementStatus.Closed, Run(D(2026, 9, 25), [purchase]).LastStatement!.Status);

        var late = Run(D(2026, 10, 2), [purchase]);
        Assert.Equal(StatementStatus.Overdue, late.LastStatement!.Status);
        Assert.True(late.NextPayment!.IsOverdue);
    }

    [Fact]
    public void The_banks_declared_total_and_minimum_win_over_the_computed_figures()
    {
        var purchase = Purchase(D(2026, 9, 10), 400m);
        var interest = Of(CreditCardMovementType.Interest, TransactionDirection.Expense, D(2026, 9, 15), 20m);
        var declared = new DeclaredStatement(D(2026, 9, 15), D(2026, 9, 30), 420m, 45m);

        var snapshot = Run(D(2026, 9, 18), [purchase, interest], declared: [declared]);

        Assert.True(snapshot.LastStatement!.IsDeclared);
        Assert.Equal(420m, snapshot.LastStatement.StatementBalance);
        Assert.Equal(45m, snapshot.LastStatement.MinimumPayment);
        Assert.Equal(45m, snapshot.NextPayment!.MinimumPayment);

        var afterPayment = Run(D(2026, 9, 25), [purchase, interest, Payment(D(2026, 9, 20), 100m)], declared: [declared]);
        Assert.Equal(320m, afterPayment.NextPayment!.Amount);
        Assert.Equal(0m, afterPayment.NextPayment.MinimumPayment);
    }

    [Fact]
    public void A_declared_closing_a_few_days_off_the_configured_one_replaces_it()
    {
        // Configured corte 15; the bank closed on the 17th (weekend shift).
        var purchase = Purchase(D(2026, 8, 20), 50m);
        var lateCharge = Purchase(D(2026, 9, 16), 30m);
        var declared = new DeclaredStatement(D(2026, 9, 17), D(2026, 10, 2), 80m, null);

        var snapshot = Run(D(2026, 9, 20), [purchase, lateCharge], declared: [declared]);

        Assert.Equal(D(2026, 9, 17), snapshot.LastStatement!.ClosingDate);
        Assert.Equal(D(2026, 10, 2), snapshot.LastStatement.DueDate);
        Assert.Equal(D(2026, 9, 18), snapshot.CurrentCycle.Start);
    }

    [Fact]
    public void A_refund_lowers_the_debt_and_interest_and_fees_raise_it()
    {
        var snapshot = Run(D(2026, 3, 10),
        [
            Purchase(D(2026, 3, 1), 100m),
            Of(CreditCardMovementType.Refund, TransactionDirection.Income, D(2026, 3, 2), 30m),
            Of(CreditCardMovementType.Interest, TransactionDirection.Expense, D(2026, 3, 3), 12.40m),
            Of(CreditCardMovementType.Fee, TransactionDirection.Expense, D(2026, 3, 3), 5m),
        ]);

        Assert.Equal(87.40m, snapshot.CurrentDebt);
        Assert.Equal(87.40m, snapshot.NextPayment!.Amount);
    }

    [Fact]
    public void Installments_commit_only_the_next_one_while_the_whole_balance_stays_visible_as_debt()
    {
        var laptop = Purchase(D(2026, 5, 10), 1200m);
        var plan = Plan(laptop, 12, firstClosing: D(2026, 5, 15));

        var movements = new List<CardMovement> { laptop };
        foreach (var month in new[] { 5, 6, 7, 8 })
        {
            movements.Add(Payment(D(2026, month, 20), 100m));
        }

        var snapshot = Run(D(2026, 9, 1), [.. movements], [plan]);

        Assert.Equal(800m, snapshot.CurrentDebt);
        Assert.Equal(800m, snapshot.DeferredDebt);
        Assert.Equal(100m, snapshot.LastStatement!.StatementBalance);
        Assert.Equal(StatementStatus.Paid, snapshot.LastStatement.Status);
        Assert.Equal(100m, snapshot.NextPayment!.Amount);
        Assert.Equal(D(2026, 9, 30), snapshot.NextPayment.DueDate);
        Assert.Equal(100m, snapshot.CommittedContribution);
    }

    [Fact]
    public void The_first_installment_is_the_whole_first_statement_not_the_purchase()
    {
        var laptop = Purchase(D(2026, 5, 10), 1200m);
        var snapshot = Run(D(2026, 5, 12), [laptop], [Plan(laptop, 12, D(2026, 5, 15))]);

        Assert.Equal(1200m, snapshot.CurrentDebt);
        Assert.Equal(100m, snapshot.ProjectedStatementBalance);
        Assert.Equal(100m, snapshot.CommittedContribution);
    }

    [Fact]
    public void Cancelling_a_plan_makes_the_remaining_balance_payable()
    {
        var laptop = Purchase(D(2026, 5, 10), 1200m);
        var plan = Plan(laptop, 12, D(2026, 5, 15)) with { CancelledOn = D(2026, 5, 12) };

        var snapshot = Run(D(2026, 5, 12), [laptop], [plan]);

        Assert.Equal(1200m, snapshot.NextPayment!.Amount);
        Assert.Equal(0m, snapshot.DeferredDebt);
    }

    [Fact]
    public void An_unpaid_statement_carries_over_into_the_next_one()
    {
        var snapshot = Run(D(2026, 10, 20),
        [
            Purchase(D(2026, 9, 10), 420m),
            Purchase(D(2026, 9, 20), 80m),
        ]);

        Assert.Equal(500m, snapshot.LastStatement!.StatementBalance);
        Assert.Equal(StatementStatus.Overdue, snapshot.Statements.Single(s => s.ClosingDate == D(2026, 9, 15)).Status);
    }

    [Fact]
    public void Card_activity_is_summarized_by_type()
    {
        var activity = CreditCardCalculator.Summarize(
        [
            Purchase(D(2026, 3, 1), 286m),
            Payment(D(2026, 3, 2), 350m),
            Of(CreditCardMovementType.Interest, TransactionDirection.Expense, D(2026, 3, 3), 12m),
            Purchase(D(2026, 2, 28), 999m),
        ], D(2026, 3, 1), D(2026, 3, 31));

        Assert.Equal(286m, activity.Purchases);
        Assert.Equal(350m, activity.Payments);
        Assert.Equal(12m, activity.Interest);
    }

    private static InstallmentPlanSchedule Plan(CardMovement purchase, int count, DateOnly firstClosing)
    {
        var amounts = InstallmentPlan.SplitEvenly(purchase.Amount, count);
        var installments = new List<ScheduledInstallment>();
        var closing = firstClosing;
        for (var i = 0; i < count; i++)
        {
            installments.Add(new ScheduledInstallment(i + 1, amounts[i], closing, BillingCalendar.DueDateFor(closing, Terms.ClosingDay, Terms.PaymentDueDay)));
            closing = BillingCalendar.NextClosing(closing, Terms.ClosingDay);
        }

        return new InstallmentPlanSchedule(Guid.CreateVersion7(), purchase.Date, installments, null, PurchaseCounts: true);
    }
}

public class CreditCardMovementTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 17, 0, 0, TimeSpan.Zero);

    private static Transaction Movement(TransactionDirection direction, decimal amount = 100m, string description = "SUPERMAXI") =>
        Transaction.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "PICHINCHA", Now, amount, direction, description, TransactionSource.Manual, Now);

    [Theory]
    [InlineData(TransactionDirection.Expense, "SUPERMAXI ALBORADA", CreditCardMovementType.Purchase)]
    [InlineData(TransactionDirection.Expense, "INTERESES ROTATIVOS", CreditCardMovementType.Interest)]
    [InlineData(TransactionDirection.Expense, "COMISION POR SERVICIO", CreditCardMovementType.Fee)]
    [InlineData(TransactionDirection.Expense, "IMPUESTO ISD", CreditCardMovementType.Fee)]
    [InlineData(TransactionDirection.Expense, "AVANCE EN EFECTIVO", CreditCardMovementType.CashAdvance)]
    [InlineData(TransactionDirection.Income, "SU PAGO GRACIAS", CreditCardMovementType.Payment)]
    [InlineData(TransactionDirection.Income, "ABONO TARJETA", CreditCardMovementType.Payment)]
    [InlineData(TransactionDirection.Income, "DEVOLUCION SUPERMAXI", CreditCardMovementType.Refund)]
    [InlineData(TransactionDirection.Income, "SUPERMAXI ALBORADA", CreditCardMovementType.Refund)]
    public void Classifies_card_movements_from_direction_and_text(TransactionDirection direction, string text, CreditCardMovementType expected) =>
        Assert.Equal(expected, CreditCardMovementRules.Classify(direction, text));

    [Theory]
    [InlineData("LAPTOP CUOTA 4/12", 4, 12)]
    [InlineData("CELULAR 07/18 CUOTAS", 7, 18)]
    [InlineData("DIFERIDO 3/6 ALMACEN", 3, 6)]
    [InlineData("CUOTA 2 DE 24 COLCHON", 2, 24)]
    public void Reads_installment_markers(string text, int number, int count) =>
        Assert.Equal(new InstallmentMarker(number, count), CreditCardMovementRules.ParseInstallmentMarker(text));

    [Theory]
    [InlineData("COMPRA 10/09 SUPERMAXI")]
    [InlineData("CUOTA 13/12")]
    [InlineData("SUPERMAXI")]
    public void Dates_and_impossible_markers_are_not_installments(string text) =>
        Assert.Null(CreditCardMovementRules.ParseInstallmentMarker(text));

    [Fact]
    public void A_card_payment_is_neutral_and_stays_neutral_when_its_bank_leg_is_unlinked()
    {
        var payment = Movement(TransactionDirection.Income, description: "PAGO TARJETA");
        payment.ClassifyAsCardMovement(CreditCardMovementType.Payment, Now);
        Assert.True(payment.IsInternalTransfer);

        payment.MarkAsInternalTransfer(Guid.CreateVersion7(), Now);
        payment.ClearInternalTransfer(Now);

        Assert.True(payment.IsInternalTransfer);
        Assert.Null(payment.InternalTransferLinkId);
    }

    [Fact]
    public void A_purchase_and_a_refund_count_as_money_flow()
    {
        var purchase = Movement(TransactionDirection.Expense);
        purchase.ClassifyAsCardMovement(CreditCardMovementType.Purchase, Now);
        var refund = Movement(TransactionDirection.Income);
        refund.ClassifyAsCardMovement(CreditCardMovementType.Refund, Now);

        Assert.False(purchase.IsInternalTransfer);
        Assert.False(refund.IsInternalTransfer);
        Assert.True(CreditCardMovementRules.IsSpending(CreditCardMovementType.Purchase));
        Assert.False(CreditCardMovementRules.IsSpending(CreditCardMovementType.Payment));
    }

    [Fact]
    public void The_type_must_agree_with_the_direction()
    {
        var charge = Movement(TransactionDirection.Expense);
        Assert.Throws<DomainException>(() => charge.ClassifyAsCardMovement(CreditCardMovementType.Payment, Now));

        var credit = Movement(TransactionDirection.Income);
        Assert.Throws<DomainException>(() => credit.ClassifyAsCardMovement(CreditCardMovementType.Interest, Now));

        // An adjustment may go either way.
        credit.ClassifyAsCardMovement(CreditCardMovementType.Adjustment, Now);
        Assert.True(credit.IsInternalTransfer);
    }

    [Fact]
    public void A_payment_cannot_be_split_but_a_card_purchase_can()
    {
        var payment = Movement(TransactionDirection.Income, 220m);
        payment.ClassifyAsCardMovement(CreditCardMovementType.Payment, Now);
        Assert.Throws<DomainException>(() => payment.Split([new SplitLine(null, 100m, null), new SplitLine(Guid.CreateVersion7(), 120m, null)], Now));

        var purchase = Movement(TransactionDirection.Expense, 120m);
        purchase.ClassifyAsCardMovement(CreditCardMovementType.Purchase, Now);
        purchase.Split([new SplitLine(Guid.CreateVersion7(), 90m, null), new SplitLine(Guid.CreateVersion7(), 30m, null)], Now);
        Assert.True(purchase.IsSplit);
        Assert.Equal(120m, purchase.Amount);
    }

    [Fact]
    public void A_split_purchase_cannot_become_a_payment()
    {
        var purchase = Movement(TransactionDirection.Expense, 120m);
        purchase.ClassifyAsCardMovement(CreditCardMovementType.Purchase, Now);
        purchase.Split([new SplitLine(Guid.CreateVersion7(), 90m, null), new SplitLine(Guid.CreateVersion7(), 30m, null)], Now);

        Assert.Throws<DomainException>(() => purchase.ClassifyAsCardMovement(CreditCardMovementType.CashAdvance, Now));
    }
}

public class InstallmentPlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 10, 17, 0, 0, TimeSpan.Zero);

    private static (CreditCard Card, Transaction Purchase) Setup(decimal amount = 1200m)
    {
        var provider = Provider.Create(ProviderCodes.Pichincha, "Banco Pichincha", "Pichincha", ProviderKind.Bank, [ConnectionMode.ManualImport], Now);
        var account = FinancialAccount.Open(Guid.CreateVersion7(), provider, "Visa Pichincha", AccountType.CreditCard, ConnectionMode.ManualImport, Now, "4582");
        var card = CreditCard.Configure(account, CardNetwork.Visa, 2000m, 15, 30, true, Now);
        var purchase = Transaction.Create(account.UserId, account.Id, "PICHINCHA", Now, amount, TransactionDirection.Expense, "LAPTOP", TransactionSource.Manual, Now);
        purchase.ClassifyAsCardMovement(CreditCardMovementType.Purchase, Now);
        return (card, purchase);
    }

    [Fact]
    public void Splits_amounts_in_exact_cents()
    {
        Assert.Equal([33.33m, 33.33m, 33.34m], InstallmentPlan.SplitEvenly(100m, 3));
        Assert.Equal(1200m, InstallmentPlan.SplitEvenly(1200m, 12).Sum());
    }

    [Fact]
    public void Creates_one_installment_per_statement_starting_with_the_purchase_cycle()
    {
        var (card, purchase) = Setup();
        var plan = InstallmentPlan.Create(card, purchase, new DateOnly(2026, 5, 10), 12, new DateOnly(2026, 5, 15), 15.5m, Now);

        var installments = plan.Installments.OrderBy(i => i.Number).ToList();
        Assert.Equal(12, installments.Count);
        Assert.Equal(100m, plan.InstallmentAmount);
        Assert.Equal(new DateOnly(2026, 5, 15), installments[0].ClosingDate);
        Assert.Equal(new DateOnly(2026, 5, 30), installments[0].DueDate);
        Assert.Equal(new DateOnly(2027, 4, 15), installments[^1].ClosingDate);
        Assert.Equal(1200m, installments.Sum(i => i.Amount));
    }

    [Fact]
    public void Only_a_card_purchase_can_be_deferred()
    {
        var (card, _) = Setup();
        var payment = Transaction.Create(card.UserId, card.FinancialAccountId, "PICHINCHA", Now, 100m, TransactionDirection.Income, "PAGO", TransactionSource.Manual, Now);
        payment.ClassifyAsCardMovement(CreditCardMovementType.Payment, Now);

        Assert.Throws<DomainException>(() =>
            InstallmentPlan.Create(card, payment, new DateOnly(2026, 5, 10), 3, new DateOnly(2026, 5, 15), null, Now));
    }

    [Fact]
    public void Rescheduling_moves_only_the_installments_not_billed_yet()
    {
        var (card, purchase) = Setup();
        var plan = InstallmentPlan.Create(card, purchase, new DateOnly(2026, 5, 10), 4, new DateOnly(2026, 5, 15), null, Now);

        plan.Reschedule(closingDay: 20, dueDay: 5, today: new DateOnly(2026, 6, 1), Now);

        var installments = plan.Installments.OrderBy(i => i.Number).ToList();
        Assert.Equal(new DateOnly(2026, 5, 15), installments[0].ClosingDate);
        Assert.Equal(new DateOnly(2026, 6, 20), installments[1].ClosingDate);
        Assert.Equal(new DateOnly(2026, 7, 5), installments[1].DueDate);
    }
}

public class CreditCardCommittedMoneyTests
{
    [Fact]
    public void A_card_payment_adds_to_committed_next_to_the_other_sources()
    {
        var result = CommittedMoneyCalculator.Calculate(668.89m,
        [
            new CommitmentCandidate(CommitmentSource.UpcomingPayment, "merchant:NETFLIX", "Netflix", null, 52.59m),
            new CommitmentCandidate(CommitmentSource.ReservedBudget, "alquiler", "Alquiler", Guid.CreateVersion7(), 45m, Guid.CreateVersion7()),
            new CommitmentCandidate(CommitmentSource.CreditCard, "card:visa", "Visa Pichincha", null, 220m, Guid.CreateVersion7()),
        ]);

        Assert.Equal(317.59m, result.Committed);
        Assert.Equal(351.30m, result.Available);
        Assert.Equal(220m, result.TotalFor(CommitmentSource.CreditCard));
    }
}
