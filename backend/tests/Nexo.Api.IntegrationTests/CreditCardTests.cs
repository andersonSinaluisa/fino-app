using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Tarjetas de crédito, end to end through the real API. The suite's clock is
/// 10 mar 2026 12:00 in Guayaquil. Unless a test says otherwise the card closes on
/// the 15th and is paid by the 30th, so the open cycle is 16 feb – 15 mar (due 30 mar).
///
/// The rules every test protects:
/// UNA COMPRA CON TARJETA ES UN GASTO. PAGAR ESA TARJETA NO ES OTRO GASTO.
/// EL CUPO DE LA TARJETA NO ES DINERO. LA DEUDA FUTURA NO ES DINERO COMPROMETIDO HOY.
/// </summary>
public class CreditCardTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ------------------------------------------------------------------ helpers

    private static async Task<Guid> CreateCardAsync(
        TestUser user,
        decimal limit = 2000m,
        int closingDay = 15,
        int dueDay = 30,
        bool autoReserve = true,
        decimal? currentDebt = null,
        string providerCode = "PICHINCHA")
    {
        var response = await user.Client.PostAsJsonAsync("/api/v1/credit-cards", new
        {
            name = "Visa Pichincha",
            providerCode,
            lastFour = "4582",
            creditLimit = limit,
            closingDay,
            paymentDueDay = dueDay,
            autoReserve,
            network = "Visa",
            currentDebt,
        });
        await response.EnsureOkAsync();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return created.GetProperty("card").GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> MovementAsync(
        TestUser user,
        Guid accountId,
        decimal amount,
        string description,
        string date = "2026-03-09T15:00:00Z",
        Guid? categoryId = null,
        string direction = "Expense",
        string? cardMovementType = null)
    {
        var response = await user.Client.PostAsJsonAsync("/api/v1/quick-entry/transactions", new
        {
            amount,
            direction,
            description,
            financialAccountId = accountId,
            categoryId,
            occurredAt = DateTimeOffset.Parse(date, System.Globalization.CultureInfo.InvariantCulture),
            cardMovementType,
        });
        await response.EnsureOkAsync();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    private static async Task<JsonElement> PayAsync(TestUser user, Guid cardId, decimal amount, Guid? from, string? date = null)
    {
        var response = await user.Client.PostAsJsonAsync($"/api/v1/credit-cards/{cardId}/payments", new
        {
            amount,
            sourceAccountId = from,
            paidAt = date is null ? (DateTimeOffset?)null : DateTimeOffset.Parse(date, System.Globalization.CultureInfo.InvariantCulture),
        });
        await response.EnsureOkAsync();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    private static async Task<Guid> CategoryAsync(TestUser user, string code)
    {
        var categories = await user.GetJsonAsync("/api/v1/categories");
        return categories.EnumerateArray().First(c => c.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> OwnCategoryAsync(TestUser user, string name)
    {
        var response = await user.Client.PostAsJsonAsync("/api/v1/categories", new { name, icon = "people", color = "#D8665B", isIncome = false });
        await response.EnsureOkAsync();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// A bank account whose verified balance was seen on 1 mar. (An anchor dated "now"
    /// would, by design, treat every movement up to "now" -- the suite's fixed clock --
    /// as already included in that balance.)
    /// </summary>
    private static async Task<Guid> BankAsync(TestUser user, decimal balance)
    {
        var bank = await user.CreateAccountAsync(openingBalance: null);
        var anchor = await user.Client.PostAsJsonAsync($"/api/v1/accounts/{bank}/verified-balance", new
        {
            balance,
            asOf = new DateTimeOffset(2026, 3, 1, 5, 0, 0, TimeSpan.Zero),
        });
        await anchor.EnsureOkAsync();
        return bank;
    }

    private static async Task<JsonElement> CardAsync(TestUser user, Guid cardId) =>
        await user.GetJsonAsync($"/api/v1/credit-cards/{cardId}");

    private static decimal Decimal(JsonElement element, string property) => element.GetProperty(property).GetDecimal();

    private static decimal CategoryTotal(JsonElement breakdown, Guid categoryId) =>
        breakdown.EnumerateArray()
            .Where(c => c.GetProperty("categoryId").GetGuid() == categoryId)
            .Select(c => c.GetProperty("total").GetDecimal())
            .SingleOrDefault();

    private static decimal CommittedSource(JsonElement committed, string type) =>
        committed.GetProperty("sources").EnumerateArray()
            .Where(s => s.GetProperty("type").GetString() == type)
            .Select(s => s.GetProperty("amount").GetDecimal())
            .SingleOrDefault();

    private static async Task<decimal> AccountBalanceAsync(TestUser user, Guid accountId) =>
        (await user.GetJsonAsync($"/api/v1/accounts/{accountId}")).GetProperty("balance").GetDecimal();

    // ------------------------------------------------------------------ the two critical tests

    [Fact]
    public async Task Test_36_a_card_purchase_is_spent_once_and_paying_the_card_is_not_a_second_expense()
    {
        var user = await factory.RegisterUserAsync();
        var bank = await BankAsync(user, 668.89m);
        var card = await CreateCardAsync(user, autoReserve: true);
        var comida = await CategoryAsync(user, "COMIDA");

        await MovementAsync(user, card, 100m, "Supermaxi", categoryId: comida);

        // Tu dinero is untouched by a card purchase; Comprometido reserves the payment.
        var committed = await user.GetJsonAsync("/api/v1/finance/committed");
        Assert.Equal(668.89m, Decimal(committed, "currentMoney"));
        Assert.Equal(100m, Decimal(committed, "committed"));
        Assert.Equal(568.89m, Decimal(committed, "available"));
        Assert.Equal(100m, CommittedSource(committed, "credit_card"));

        var summary = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(668.89m, Decimal(summary, "totalBalance"));
        Assert.Equal(100m, summary.GetProperty("month").GetProperty("expense").GetDecimal());
        Assert.Equal(668.89m, await AccountBalanceAsync(user, bank));

        var detail = await CardAsync(user, card);
        Assert.Equal(100m, Decimal(detail.GetProperty("card"), "currentDebt"));

        // Pay the card from the bank.
        await PayAsync(user, card, 100m, bank);

        Assert.Equal(568.89m, await AccountBalanceAsync(user, bank));
        detail = await CardAsync(user, card);
        Assert.Equal(0m, Decimal(detail.GetProperty("card"), "currentDebt"));
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("card").GetProperty("nextPayment").ValueKind);

        committed = await user.GetJsonAsync("/api/v1/finance/committed");
        Assert.Equal(568.89m, Decimal(committed, "currentMoney"));
        Assert.Equal(0m, Decimal(committed, "committed"));
        Assert.Equal(568.89m, Decimal(committed, "available"));

        // Gastos: STILL $100, never $200. And the payment is not income either.
        summary = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(100m, summary.GetProperty("month").GetProperty("expense").GetDecimal());
        Assert.Equal(0m, summary.GetProperty("month").GetProperty("income").GetDecimal());
        Assert.Equal(100m, CategoryTotal(summary.GetProperty("categoryBreakdown"), comida));

        var dashboard = await user.GetJsonAsync("/api/v1/analytics/dashboard?period=month");
        Assert.Equal(100m, dashboard.GetProperty("kpis").GetProperty("expense").GetDecimal());
        Assert.Equal(0m, dashboard.GetProperty("kpis").GetProperty("income").GetDecimal());
    }

    [Fact]
    public async Task Test_37_a_split_card_purchase_counts_220_once_by_category_and_its_payment_never_makes_it_440()
    {
        var user = await factory.RegisterUserAsync();
        var bank = await BankAsync(user, 1000m);
        var card = await CreateCardAsync(user);
        var comida = await CategoryAsync(user, "COMIDA");
        var esposa = await OwnCategoryAsync(user, "Esposa");

        var purchase = await MovementAsync(user, card, 220m, "TRANSF DIRECTA CHILAN", categoryId: comida);
        var split = await user.Client.PutAsJsonAsync($"/api/v1/transactions/{purchase.GetProperty("id").GetGuid()}/splits", new
        {
            splits = new object[] { new { categoryId = esposa, amount = 70m }, new { categoryId = comida, amount = 150m } },
        });
        await split.EnsureOkAsync();

        var detail = await CardAsync(user, card);
        Assert.Equal(220m, Decimal(detail.GetProperty("card"), "currentDebt"));

        async Task AssertSpending()
        {
            var summary = await user.GetJsonAsync("/api/v1/summary");
            Assert.Equal(220m, summary.GetProperty("month").GetProperty("expense").GetDecimal());
            var breakdown = summary.GetProperty("categoryBreakdown");
            Assert.Equal(70m, CategoryTotal(breakdown, esposa));
            Assert.Equal(150m, CategoryTotal(breakdown, comida));

            var dashboard = await user.GetJsonAsync("/api/v1/analytics/dashboard?period=month");
            Assert.Equal(220m, dashboard.GetProperty("kpis").GetProperty("expense").GetDecimal());
            Assert.Equal(150m, CategoryTotal(dashboard.GetProperty("categoryBreakdown"), comida));
        }

        await AssertSpending();

        await PayAsync(user, card, 220m, bank);

        Assert.Equal(780m, await AccountBalanceAsync(user, bank));
        Assert.Equal(0m, Decimal((await CardAsync(user, card)).GetProperty("card"), "currentDebt"));
        await AssertSpending();
    }

    // ------------------------------------------------------------------ money vs debt

    [Fact]
    public async Task The_available_credit_is_never_money()
    {
        var user = await factory.RegisterUserAsync();
        await BankAsync(user, 700m);
        var cash = await user.Client.PostAsJsonAsync("/api/v1/quick-entry/cash-balance", new { balance = 100m, mode = "Anchor" });
        await cash.EnsureOkAsync();
        var card = await CreateCardAsync(user, limit: 2000m, autoReserve: false);

        var summary = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(800m, Decimal(summary, "totalBalance"));

        var committed = await user.GetJsonAsync("/api/v1/finance/committed");
        Assert.Equal(800m, Decimal(committed, "currentMoney"));
        Assert.Equal(800m, Decimal(committed, "available"));

        var detail = (await CardAsync(user, card)).GetProperty("card");
        Assert.Equal(2000m, Decimal(detail, "availableCredit"));
        Assert.Equal(0m, Decimal(detail, "currentDebt"));

        var accounts = await user.GetJsonAsync("/api/v1/accounts");
        Assert.True(accounts.EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == card).GetProperty("isLiability").GetBoolean());
    }

    [Fact]
    public async Task A_new_card_shows_debt_next_payment_due_date_and_utilization()
    {
        var user = await factory.RegisterUserAsync();
        var card = await CreateCardAsync(user, limit: 2000m, currentDebt: 750.32m);

        var detail = await CardAsync(user, card);
        var summary = detail.GetProperty("card");
        Assert.Equal("Visa Pichincha", summary.GetProperty("name").GetString());
        Assert.Equal("4582", summary.GetProperty("lastFour").GetString());
        Assert.Equal(750.32m, Decimal(summary, "currentDebt"));
        Assert.Equal(1249.68m, Decimal(summary, "availableCredit"));
        Assert.Equal(37.5m, Decimal(summary, "utilizationPercent"));
        Assert.Equal(750.32m, Decimal(summary.GetProperty("nextPayment"), "amount"));
        Assert.Equal("2026-03-30", summary.GetProperty("nextPayment").GetProperty("dueDate").GetString());
        Assert.Equal("2026-03-15", detail.GetProperty("currentCycle").GetProperty("closing").GetString());
        Assert.False(summary.GetProperty("needsSetup").GetBoolean());
    }

    [Fact]
    public async Task The_full_card_number_is_never_accepted()
    {
        var user = await factory.RegisterUserAsync();
        var response = await user.Client.PostAsJsonAsync("/api/v1/credit-cards", new
        {
            name = "Visa",
            providerCode = "PICHINCHA",
            lastFour = "4111111111114582",
            creditLimit = 1000m,
            closingDay = 15,
            paymentDueDay = 30,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await user.GetJsonAsync("/api/v1/accounts")).EnumerateArray());
    }

    [Fact]
    public async Task Without_auto_reserve_the_debt_is_visible_but_does_not_reduce_available()
    {
        var user = await factory.RegisterUserAsync();
        await BankAsync(user, 500m);
        var card = await CreateCardAsync(user, autoReserve: false);
        await MovementAsync(user, card, 120m, "Supermaxi");

        var committed = await user.GetJsonAsync("/api/v1/finance/committed");
        Assert.Equal(0m, Decimal(committed, "committed"));
        Assert.Equal(500m, Decimal(committed, "available"));
        Assert.Equal(120m, Decimal((await CardAsync(user, card)).GetProperty("card"), "currentDebt"));

        // Turning it on reserves exactly the next payment.
        var update = await user.Client.PutAsJsonAsync($"/api/v1/credit-cards/{card}", new
        {
            creditLimit = 2000m,
            closingDay = 15,
            paymentDueDay = 30,
            autoReserve = true,
        });
        await update.EnsureOkAsync();
        committed = await user.GetJsonAsync("/api/v1/finance/committed");
        Assert.Equal(120m, Decimal(committed, "committed"));
        Assert.Equal(380m, Decimal(committed, "available"));
        var item = committed.GetProperty("sources").EnumerateArray().Single(s => s.GetProperty("type").GetString() == "credit_card")
            .GetProperty("items")[0];
        Assert.Equal("Visa Pichincha", item.GetProperty("label").GetString());
        Assert.Equal("2026-03-30", item.GetProperty("expectedDate").GetString());
        Assert.Equal(card, item.GetProperty("creditCardId").GetGuid());
    }

    [Fact]
    public async Task Card_committed_money_sits_next_to_budgets_without_double_counting()
    {
        var user = await factory.RegisterUserAsync();
        await BankAsync(user, 668.89m);
        var card = await CreateCardAsync(user);
        var comida = await CategoryAsync(user, "COMIDA");

        var budget = await user.Client.PostAsJsonAsync("/api/v1/budgets", new { amount = 300m, categoryId = comida, reserveFunds = true });
        await budget.EnsureOkAsync();

        await MovementAsync(user, card, 100m, "Supermaxi", categoryId: comida);

        // Budget: 300 − 100 spent = 200 still reserved; card: 100 to pay. Total 300.
        var committed = await user.GetJsonAsync("/api/v1/finance/committed");
        Assert.Equal(200m, CommittedSource(committed, "reserved_budget"));
        Assert.Equal(100m, CommittedSource(committed, "credit_card"));
        Assert.Equal(300m, Decimal(committed, "committed"));

        var budgets = await user.GetJsonAsync("/api/v1/budgets");
        Assert.Equal(100m, budgets.GetProperty("budgets")[0].GetProperty("progress").GetProperty("spent").GetDecimal());
    }

    // ------------------------------------------------------------------ statements and payments

    [Fact]
    public async Task A_partial_payment_leaves_the_rest_pending_and_paying_the_rest_marks_it_paid()
    {
        // Corte 5, pago 25: the statement that closed on 5 mar is due 25 mar.
        var user = await factory.RegisterUserAsync();
        var bank = await BankAsync(user, 1000m);
        var card = await CreateCardAsync(user, closingDay: 5, dueDay: 25);
        await MovementAsync(user, card, 420m, "LAPTOP STORE", date: "2026-03-01T15:00:00Z");

        var statements = await user.GetJsonAsync($"/api/v1/credit-cards/{card}/statements");
        var closed = statements.EnumerateArray().Single(s => s.GetProperty("closingDate").GetString() == "2026-03-05");
        Assert.Equal(420m, Decimal(closed, "statementBalance"));
        Assert.Equal("Closed", closed.GetProperty("status").GetString());
        Assert.Equal("Open", statements[0].GetProperty("status").GetString());

        await PayAsync(user, card, 100m, bank, "2026-03-08T15:00:00Z");
        var detail = await CardAsync(user, card);
        var last = detail.GetProperty("lastStatement");
        Assert.Equal(100m, Decimal(last, "amountPaid"));
        Assert.Equal(320m, Decimal(last, "pending"));
        Assert.Equal("PartiallyPaid", last.GetProperty("status").GetString());
        Assert.Equal(320m, Decimal(detail.GetProperty("card").GetProperty("nextPayment"), "amount"));
        Assert.Equal("statement", detail.GetProperty("card").GetProperty("nextPayment").GetProperty("source").GetString());

        await PayAsync(user, card, 320m, bank, "2026-03-09T15:00:00Z");
        detail = await CardAsync(user, card);
        Assert.Equal("Paid", detail.GetProperty("lastStatement").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("card").GetProperty("nextPayment").ValueKind);
        Assert.Equal(580m, await AccountBalanceAsync(user, bank));
    }

    [Fact]
    public async Task The_banks_total_and_minimum_are_shown_and_paying_the_minimum_leaves_the_rest_pending()
    {
        var user = await factory.RegisterUserAsync();
        var bank = await BankAsync(user, 1000m);
        var card = await CreateCardAsync(user, closingDay: 5, dueDay: 25);
        await MovementAsync(user, card, 400m, "ALMACEN", date: "2026-03-01T15:00:00Z");

        var declare = await user.Client.PutAsJsonAsync($"/api/v1/credit-cards/{card}/statements", new
        {
            closingDate = "2026-03-05",
            dueDate = "2026-03-25",
            statementBalance = 420m,
            minimumPayment = 45m,
        });
        await declare.EnsureOkAsync();

        var next = (await CardAsync(user, card)).GetProperty("card").GetProperty("nextPayment");
        Assert.Equal(420m, Decimal(next, "amount"));
        Assert.Equal(45m, Decimal(next, "minimumPayment"));

        await PayAsync(user, card, 45m, bank, "2026-03-09T15:00:00Z");
        var detail = await CardAsync(user, card);
        Assert.Equal(375m, Decimal(detail.GetProperty("lastStatement"), "pending"));
        Assert.True(detail.GetProperty("lastStatement").GetProperty("isDeclared").GetBoolean());
        Assert.Equal(0m, Decimal(detail.GetProperty("card").GetProperty("nextPayment"), "minimumPayment"));
    }

    [Fact]
    public async Task An_unpaid_statement_past_its_due_date_is_overdue()
    {
        // Corte 15, pago 28: the statement that closed 15 feb was due 28 feb.
        var user = await factory.RegisterUserAsync();
        var card = await CreateCardAsync(user, closingDay: 15, dueDay: 28);
        await MovementAsync(user, card, 60m, "FARMACIA", date: "2026-02-10T15:00:00Z");

        var detail = await CardAsync(user, card);
        Assert.Equal("Overdue", detail.GetProperty("lastStatement").GetProperty("status").GetString());
        Assert.True(detail.GetProperty("card").GetProperty("nextPayment").GetProperty("isOverdue").GetBoolean());
    }

    [Fact]
    public async Task Movements_belong_to_the_statement_of_their_cycle_including_the_closing_day()
    {
        var user = await factory.RegisterUserAsync();
        var card = await CreateCardAsync(user, closingDay: 5, dueDay: 25);
        await MovementAsync(user, card, 10m, "A", date: "2026-03-05T20:00:00Z"); // 5 mar local: closing day
        await MovementAsync(user, card, 20m, "B", date: "2026-03-06T15:00:00Z"); // next cycle

        var statements = await user.GetJsonAsync($"/api/v1/credit-cards/{card}/statements");
        var closed = statements.EnumerateArray().Single(s => s.GetProperty("closingDate").GetString() == "2026-03-05");
        Assert.Equal(10m, Decimal(closed, "charges"));
        Assert.Equal(20m, Decimal(statements[0], "charges"));
        Assert.Equal("2026-04-05", statements[0].GetProperty("closingDate").GetString());
    }

    [Fact]
    public async Task A_card_closing_on_day_31_closes_on_the_last_day_of_the_month()
    {
        var user = await factory.RegisterUserAsync();
        var card = await CreateCardAsync(user, closingDay: 31, dueDay: 20);

        var detail = await CardAsync(user, card);
        Assert.Equal("2026-03-01", detail.GetProperty("currentCycle").GetProperty("start").GetString());
        Assert.Equal("2026-03-31", detail.GetProperty("currentCycle").GetProperty("closing").GetString());
        Assert.Equal("2026-04-20", detail.GetProperty("currentCycle").GetProperty("due").GetString());
    }

    // ------------------------------------------------------------------ refunds, interest, fees

    [Fact]
    public async Task A_refund_lowers_the_debt_and_the_category_and_is_never_income()
    {
        var user = await factory.RegisterUserAsync();
        var card = await CreateCardAsync(user);
        var comida = await CategoryAsync(user, "COMIDA");
        var budget = await user.Client.PostAsJsonAsync("/api/v1/budgets", new { amount = 300m, categoryId = comida });
        await budget.EnsureOkAsync();

        await MovementAsync(user, card, 100m, "RESTAURANTE", categoryId: comida);
        var refund = await MovementAsync(user, card, 30m, "DEVOLUCION RESTAURANTE", categoryId: comida, direction: "Income");
        Assert.Equal("Refund", refund.GetProperty("cardMovementType").GetString());

        Assert.Equal(70m, Decimal((await CardAsync(user, card)).GetProperty("card"), "currentDebt"));

        var summary = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(70m, summary.GetProperty("month").GetProperty("expense").GetDecimal());
        Assert.Equal(0m, summary.GetProperty("month").GetProperty("income").GetDecimal());
        Assert.Equal(70m, CategoryTotal(summary.GetProperty("categoryBreakdown"), comida));

        var dashboard = await user.GetJsonAsync("/api/v1/analytics/dashboard?period=month");
        Assert.Equal(70m, dashboard.GetProperty("kpis").GetProperty("expense").GetDecimal());
        Assert.Equal(0m, dashboard.GetProperty("kpis").GetProperty("income").GetDecimal());

        var budgets = await user.GetJsonAsync("/api/v1/budgets");
        Assert.Equal(70m, budgets.GetProperty("budgets")[0].GetProperty("progress").GetProperty("spent").GetDecimal());
    }

    [Fact]
    public async Task Interest_and_fees_raise_the_debt_and_are_kept_apart_from_consumption()
    {
        var user = await factory.RegisterUserAsync();
        var card = await CreateCardAsync(user);
        var intereses = await CategoryAsync(user, "INTERESES");
        var comisiones = await CategoryAsync(user, "COMISIONES_IMPUESTOS");

        await MovementAsync(user, card, 100m, "SUPERMAXI");
        var interest = await MovementAsync(user, card, 12.40m, "INTERESES ROTATIVOS");
        var fee = await MovementAsync(user, card, 5m, "COMISION MEMBRESIA");

        Assert.Equal("Interest", interest.GetProperty("cardMovementType").GetString());
        Assert.Equal(intereses, interest.GetProperty("categoryId").GetGuid());
        Assert.Equal("Fee", fee.GetProperty("cardMovementType").GetString());
        Assert.Equal(comisiones, fee.GetProperty("categoryId").GetGuid());

        var detail = await CardAsync(user, card);
        Assert.Equal(117.40m, Decimal(detail.GetProperty("card"), "currentDebt"));
        var month = detail.GetProperty("thisMonth");
        Assert.Equal(100m, Decimal(month, "purchases"));
        Assert.Equal(12.40m, Decimal(month, "interest"));
        Assert.Equal(5m, Decimal(month, "fees"));

        var summary = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(117.40m, summary.GetProperty("month").GetProperty("expense").GetDecimal());
        Assert.Equal(12.40m, CategoryTotal(summary.GetProperty("categoryBreakdown"), intereses));
    }

    // ------------------------------------------------------------------ installments

    [Fact]
    public async Task A_deferred_purchase_is_one_expense_and_only_the_next_installment_is_committed()
    {
        var user = await factory.RegisterUserAsync();
        await BankAsync(user, 2000m);
        var card = await CreateCardAsync(user, limit: 3000m);
        var laptop = await MovementAsync(user, card, 1200m, "LAPTOP");

        var response = await user.Client.PostAsJsonAsync($"/api/v1/credit-cards/{card}/installments", new
        {
            transactionId = laptop.GetProperty("id").GetGuid(),
            numberOfInstallments = 12,
        });
        await response.EnsureOkAsync();
        var plan = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

        Assert.Equal(100m, Decimal(plan, "installmentAmount"));
        Assert.Equal(1200m, Decimal(plan, "outstandingAmount"));
        Assert.Equal(1, plan.GetProperty("nextInstallmentNumber").GetInt32());
        Assert.Equal("2026-03-15", plan.GetProperty("nextInstallmentClosingDate").GetString());
        Assert.Equal(12, plan.GetProperty("installments").GetArrayLength());

        var detail = await CardAsync(user, card);
        Assert.Equal(1200m, Decimal(detail.GetProperty("card"), "currentDebt"));
        Assert.Equal(1200m, Decimal(detail.GetProperty("card"), "deferredDebt"));
        Assert.Equal(100m, Decimal(detail.GetProperty("card").GetProperty("nextPayment"), "amount"));
        Assert.Single(detail.GetProperty("activeInstallments").EnumerateArray());

        var committed = await user.GetJsonAsync("/api/v1/finance/committed");
        Assert.Equal(100m, CommittedSource(committed, "credit_card"));
        Assert.Equal(1900m, Decimal(committed, "available"));

        // The laptop is spent ONCE, on the purchase date.
        var summary = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(1200m, summary.GetProperty("month").GetProperty("expense").GetDecimal());

        var movement = await user.GetJsonAsync($"/api/v1/transactions/{laptop.GetProperty("id").GetGuid()}");
        Assert.Equal(plan.GetProperty("id").GetGuid(), movement.GetProperty("installmentPlanId").GetGuid());

        // Precancelar: the rest becomes payable now.
        var cancel = await user.Client.PostAsync($"/api/v1/credit-cards/{card}/installments/{plan.GetProperty("id").GetGuid()}/cancel", null);
        await cancel.EnsureOkAsync();
        committed = await user.GetJsonAsync("/api/v1/finance/committed");
        Assert.Equal(1200m, CommittedSource(committed, "credit_card"));
    }

    [Fact]
    public async Task Only_a_card_purchase_can_be_deferred_and_only_once()
    {
        var user = await factory.RegisterUserAsync();
        var bank = await BankAsync(user, 500m);
        var card = await CreateCardAsync(user);
        var purchase = await MovementAsync(user, card, 300m, "CELULAR");
        var bankExpense = await MovementAsync(user, bank, 50m, "ALMUERZO");

        var first = await user.Client.PostAsJsonAsync($"/api/v1/credit-cards/{card}/installments", new { transactionId = purchase.GetProperty("id").GetGuid(), numberOfInstallments = 3 });
        await first.EnsureOkAsync();

        var again = await user.Client.PostAsJsonAsync($"/api/v1/credit-cards/{card}/installments", new { transactionId = purchase.GetProperty("id").GetGuid(), numberOfInstallments = 6 });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var notCard = await user.Client.PostAsJsonAsync($"/api/v1/credit-cards/{card}/installments", new { transactionId = bankExpense.GetProperty("id").GetGuid(), numberOfInstallments = 3 });
        Assert.Equal(HttpStatusCode.NotFound, notCard.StatusCode);
    }

    // ------------------------------------------------------------------ edit, delete, archive

    [Fact]
    public async Task Editing_a_card_purchase_updates_the_debt_and_deleting_a_payment_removes_both_legs()
    {
        var user = await factory.RegisterUserAsync();
        var bank = await BankAsync(user, 500m);
        var card = await CreateCardAsync(user);
        var purchase = await MovementAsync(user, card, 80m, "UBER");

        var edit = await user.Client.PutAsJsonAsync($"/api/v1/quick-entry/transactions/{purchase.GetProperty("id").GetGuid()}", new
        {
            amount = 95m,
            direction = "Expense",
            description = "UBER",
            occurredAt = "2026-03-09T15:00:00Z",
        });
        await edit.EnsureOkAsync();
        Assert.Equal(95m, Decimal((await CardAsync(user, card)).GetProperty("card"), "currentDebt"));

        var payment = await PayAsync(user, card, 95m, bank);
        Assert.Equal(405m, await AccountBalanceAsync(user, bank));

        var undo = await user.Client.DeleteAsync($"/api/v1/quick-entry/transactions/{payment.GetProperty("cardTransactionId").GetGuid()}");
        await undo.EnsureOkAsync();

        Assert.Equal(500m, await AccountBalanceAsync(user, bank));
        Assert.Equal(95m, Decimal((await CardAsync(user, card)).GetProperty("card"), "currentDebt"));
        var summary = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(95m, summary.GetProperty("month").GetProperty("expense").GetDecimal());
        var list = await user.GetJsonAsync($"/api/v1/transactions?accountId={bank}");
        Assert.Equal(0, list.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task An_archived_card_keeps_its_history_and_leaves_comprometido()
    {
        var user = await factory.RegisterUserAsync();
        await BankAsync(user, 500m);
        var card = await CreateCardAsync(user);
        await MovementAsync(user, card, 120m, "SUPERMAXI");

        var archive = await user.Client.DeleteAsync($"/api/v1/credit-cards/{card}");
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);

        var list = await user.GetJsonAsync("/api/v1/credit-cards");
        Assert.Empty(list.GetProperty("cards").EnumerateArray());
        var withArchived = await user.GetJsonAsync("/api/v1/credit-cards?includeArchived=true");
        Assert.True(withArchived.GetProperty("cards")[0].GetProperty("isArchived").GetBoolean());

        var committed = await user.GetJsonAsync("/api/v1/finance/committed");
        Assert.Equal(0m, CommittedSource(committed, "credit_card"));

        // Movements and their history stay.
        var movements = await user.GetJsonAsync($"/api/v1/transactions?accountId={card}");
        Assert.Equal(1, movements.GetProperty("totalCount").GetInt32());

        var restore = await user.Client.PostAsync($"/api/v1/credit-cards/{card}/restore", null);
        await restore.EnsureOkAsync();
        committed = await user.GetJsonAsync("/api/v1/finance/committed");
        Assert.Equal(120m, CommittedSource(committed, "credit_card"));
    }

    [Fact]
    public async Task Another_users_card_is_never_visible()
    {
        var owner = await factory.RegisterUserAsync();
        var card = await CreateCardAsync(owner);
        var other = await factory.RegisterUserAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/v1/credit-cards/{card}")).StatusCode);
        var pay = await other.Client.PostAsJsonAsync($"/api/v1/credit-cards/{card}/payments", new { amount = 10m });
        Assert.Equal(HttpStatusCode.NotFound, pay.StatusCode);
    }

    // ------------------------------------------------------------------ transfers, conciliation, imports

    [Fact]
    public async Task Confirming_a_bank_to_card_transfer_makes_it_a_card_payment()
    {
        var user = await factory.RegisterUserAsync();
        var bank = await BankAsync(user, 500m);
        var card = await CreateCardAsync(user);
        await MovementAsync(user, card, 200m, "ALMACEN");
        var debit = await MovementAsync(user, bank, 200m, "TRANSFERENCIA A TARJETA");
        var credit = await MovementAsync(user, card, 200m, "ABONO RECIBIDO", direction: "Income", cardMovementType: "Refund");

        var confirm = await user.Client.PostAsJsonAsync("/api/v1/transfers/confirm", new
        {
            outgoingTransactionId = debit.GetProperty("id").GetGuid(),
            incomingTransactionId = credit.GetProperty("id").GetGuid(),
        });
        await confirm.EnsureOkAsync();

        var cardLeg = await user.GetJsonAsync($"/api/v1/transactions/{credit.GetProperty("id").GetGuid()}");
        Assert.Equal("Payment", cardLeg.GetProperty("cardMovementType").GetString());

        var summary = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(200m, summary.GetProperty("month").GetProperty("expense").GetDecimal());
        Assert.Equal(0m, summary.GetProperty("month").GetProperty("income").GetDecimal());
    }

    [Fact]
    public async Task A_bank_debit_that_looks_like_a_card_payment_is_suggested_and_linking_it_stops_it_counting_as_spending()
    {
        var user = await factory.RegisterUserAsync();
        var bank = await BankAsync(user, 500m);
        var card = await CreateCardAsync(user);
        await MovementAsync(user, card, 150m, "ALMACEN");
        var debit = await MovementAsync(user, bank, 150m, "PAGO TARJETA VISA 4582");

        var before = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(300m, before.GetProperty("month").GetProperty("expense").GetDecimal());

        var suggestions = await user.GetJsonAsync($"/api/v1/credit-cards/{card}/payments/suggestions");
        var suggestion = suggestions.EnumerateArray().Single();
        Assert.Equal(debit.GetProperty("id").GetGuid(), suggestion.GetProperty("bankTransactionId").GetGuid());
        Assert.Equal("looks_like_card_payment", suggestion.GetProperty("reason").GetString());

        var link = await user.Client.PostAsJsonAsync($"/api/v1/credit-cards/{card}/payments/link", new { bankTransactionId = debit.GetProperty("id").GetGuid() });
        await link.EnsureOkAsync();

        var after = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(150m, after.GetProperty("month").GetProperty("expense").GetDecimal());
        Assert.Equal(0m, Decimal((await CardAsync(user, card)).GetProperty("card"), "currentDebt"));
        Assert.Empty((await user.GetJsonAsync($"/api/v1/credit-cards/{card}/payments/suggestions")).EnumerateArray());
    }

    [Fact]
    public async Task A_registered_payment_found_again_in_the_bank_statement_is_not_duplicated()
    {
        var user = await factory.RegisterUserAsync();
        var bank = await user.CreateAccountAsync(openingBalance: null);
        var card = await CreateCardAsync(user);
        await MovementAsync(user, card, 220m, "ALMACEN");
        await PayAsync(user, card, 220m, bank, "2026-03-08T15:00:00Z");

        var csv = "Banco Pichincha\nFecha,Concepto,Documento,Debito,Credito\n09/03/2026,PAGO TARJETA VISA,DOC-9001,220.00,";
        var preview = await user.UploadStatementAsync(bank, csv);
        Assert.Equal(1, preview.GetProperty("probableDuplicateRows").GetInt32());
        await user.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());

        Assert.Equal(-220m, await AccountBalanceAsync(user, bank));
        var summary = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(220m, summary.GetProperty("month").GetProperty("expense").GetDecimal());
    }

    [Fact]
    public async Task Importing_a_card_statement_fixes_the_signs_types_and_declares_the_statement()
    {
        var user = await factory.RegisterUserAsync();
        var card = await CreateCardAsync(user, closingDay: 5, dueDay: 25, providerCode: "OTRO");

        var csv = string.Join('\n',
            "Estado de cuenta tarjeta",
            "Fecha de corte,05/03/2026",
            "Fecha máxima de pago,25/03/2026",
            "Total a pagar,167.40",
            "Pago mínimo,20.00",
            "Cupo total,2500.00",
            "Fecha,Descripción,Valor",
            "10/02/2026,SUPERMAXI ALBORADA,120.00",
            "12/02/2026,SU PAGO GRACIAS,-50.00",
            "20/02/2026,UBER TRIP,30.00",
            "25/02/2026,DEVOLUCION UBER,-10.00",
            "28/02/2026,INTERESES ROTATIVOS,12.40",
            "01/03/2026,COMISION MEMBRESIA,5.00");

        var preview = await user.UploadStatementAsync(card, csv);
        var statement = preview.GetProperty("cardStatement");
        Assert.Equal("2026-03-05", statement.GetProperty("closingDate").GetString());
        Assert.Equal(167.40m, Decimal(statement, "statementBalance"));
        await user.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());

        var movements = (await user.GetJsonAsync($"/api/v1/transactions?accountId={card}")).GetProperty("items").EnumerateArray().ToList();
        string TypeOf(string description) => movements.Single(m => m.GetProperty("description").GetString() == description).GetProperty("cardMovementType").GetString()!;
        string DirectionOf(string description) => movements.Single(m => m.GetProperty("description").GetString() == description).GetProperty("direction").GetString()!;

        Assert.Equal("Purchase", TypeOf("SUPERMAXI ALBORADA"));
        Assert.Equal("Expense", DirectionOf("SUPERMAXI ALBORADA"));
        Assert.Equal("Payment", TypeOf("SU PAGO GRACIAS"));
        Assert.Equal("Income", DirectionOf("SU PAGO GRACIAS"));
        Assert.Equal("Refund", TypeOf("DEVOLUCION UBER"));
        Assert.Equal("Interest", TypeOf("INTERESES ROTATIVOS"));
        Assert.Equal("Fee", TypeOf("COMISION MEMBRESIA"));

        var detail = await CardAsync(user, card);
        Assert.Equal(107.40m, Decimal(detail.GetProperty("card"), "currentDebt"));
        Assert.Equal(2500m, Decimal(detail.GetProperty("card"), "creditLimit"));
        var last = detail.GetProperty("lastStatement");
        Assert.True(last.GetProperty("isDeclared").GetBoolean());
        Assert.Equal("Imported", last.GetProperty("declaredSource").GetString());
        Assert.Equal(167.40m, Decimal(last, "statementBalance"));
        Assert.Equal(20m, Decimal(last, "minimumPayment"));

        // The card payment in the file is not income; the refund lowers spending.
        var dashboard = await user.GetJsonAsync("/api/v1/analytics/dashboard?period=last_3_months");
        Assert.Equal(0m, dashboard.GetProperty("kpis").GetProperty("income").GetDecimal());
        Assert.Equal(157.40m, dashboard.GetProperty("kpis").GetProperty("expense").GetDecimal());
    }

    [Fact]
    public async Task A_purchase_written_by_hand_and_found_again_in_the_card_statement_is_not_counted_twice()
    {
        var user = await factory.RegisterUserAsync();
        var card = await CreateCardAsync(user, providerCode: "OTRO");
        await MovementAsync(user, card, 42.80m, "Supermaxi", date: "2026-03-05T15:00:00Z");

        var csv = "Fecha,Descripción,Valor\n05/03/2026,SUPERMERCADO SUPERMAXI,42.80\n06/03/2026,KFC,10.00";
        var preview = await user.UploadStatementAsync(card, csv);
        Assert.Equal(1, preview.GetProperty("probableDuplicateRows").GetInt32() + preview.GetProperty("duplicateRows").GetInt32());
        await user.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());

        Assert.Equal(52.80m, Decimal((await CardAsync(user, card)).GetProperty("card"), "currentDebt"));
        var summary = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(52.80m, summary.GetProperty("month").GetProperty("expense").GetDecimal());
    }

    [Fact]
    public async Task An_installment_line_of_a_purchase_already_deferred_in_fino_is_not_imported_again()
    {
        var user = await factory.RegisterUserAsync();
        var card = await CreateCardAsync(user, limit: 3000m, providerCode: "OTRO");
        var laptop = await MovementAsync(user, card, 1200m, "LAPTOP", date: "2026-03-01T15:00:00Z");
        var plan = await user.Client.PostAsJsonAsync($"/api/v1/credit-cards/{card}/installments", new { transactionId = laptop.GetProperty("id").GetGuid(), numberOfInstallments = 12 });
        await plan.EnsureOkAsync();

        var csv = "Fecha,Descripción,Valor\n01/03/2026,LAPTOP CUOTA 1/12,100.00\n06/03/2026,KFC,10.00";
        var preview = await user.UploadStatementAsync(card, csv);
        var skipped = preview.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("description").GetString() == "LAPTOP CUOTA 1/12");
        Assert.Equal("Skipped", skipped.GetProperty("status").GetString());
        Assert.Contains("1/12", skipped.GetProperty("skipReason").GetString());
        await user.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());

        Assert.Equal(1210m, Decimal((await CardAsync(user, card)).GetProperty("card"), "currentDebt"));
        var summary = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(1210m, summary.GetProperty("month").GetProperty("expense").GetDecimal());
    }

    [Fact]
    public async Task A_card_account_created_before_this_module_is_debt_and_can_be_completed()
    {
        var user = await factory.RegisterUserAsync();
        await BankAsync(user, 300m);
        var response = await user.Client.PostAsJsonAsync("/api/v1/accounts", new
        {
            providerCode = "PICHINCHA",
            alias = "Mastercard",
            accountType = "CreditCard",
            connectionMode = "ManualImport",
            openingVerifiedBalance = -320m,
            currency = "USD",
        });
        await response.EnsureOkAsync();
        var card = (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        var summary = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(300m, Decimal(summary, "totalBalance"));

        var list = await user.GetJsonAsync("/api/v1/credit-cards");
        var legacy = list.GetProperty("cards")[0];
        Assert.True(legacy.GetProperty("needsSetup").GetBoolean());
        Assert.Equal(320m, Decimal(legacy, "currentDebt"));

        var complete = await user.Client.PutAsJsonAsync($"/api/v1/credit-cards/{card}", new { creditLimit = 1000m, closingDay = 15, paymentDueDay = 30, autoReserve = true });
        await complete.EnsureOkAsync();
        var detail = await CardAsync(user, card);
        Assert.False(detail.GetProperty("card").GetProperty("needsSetup").GetBoolean());
        Assert.Equal(680m, Decimal(detail.GetProperty("card"), "availableCredit"));
    }

    [Fact]
    public async Task A_misread_movement_can_be_reclassified_and_a_payment_can_never_be_split()
    {
        var user = await factory.RegisterUserAsync();
        var card = await CreateCardAsync(user);
        var credit = await MovementAsync(user, card, 40m, "ALMACEN XYZ", direction: "Income");
        Assert.Equal("Refund", credit.GetProperty("cardMovementType").GetString());

        var reclassify = await user.Client.PostAsJsonAsync($"/api/v1/credit-cards/{card}/movements/{credit.GetProperty("id").GetGuid()}/type", new { type = "Payment" });
        await reclassify.EnsureOkAsync();
        var updated = await reclassify.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("Payment", updated.GetProperty("cardMovementType").GetString());
        Assert.True(updated.GetProperty("isInternalTransfer").GetBoolean());

        var split = await user.Client.PutAsJsonAsync($"/api/v1/transactions/{credit.GetProperty("id").GetGuid()}/splits", new
        {
            splits = new object[] { new { categoryId = (Guid?)null, amount = 20m }, new { categoryId = await CategoryAsync(user, "COMIDA"), amount = 20m } },
        });
        Assert.False(split.IsSuccessStatusCode);
    }

    [Fact]
    public async Task The_app_sends_only_the_type_and_the_backend_decides_whether_it_raises_or_lowers_the_debt()
    {
        var user = await factory.RegisterUserAsync();
        var card = await CreateCardAsync(user);

        var response = await user.Client.PostAsJsonAsync("/api/v1/quick-entry/transactions", new
        {
            amount = 12.40m,
            financialAccountId = card,
            cardMovementType = "Interest",
            description = "Intereses",
        });
        await response.EnsureOkAsync();
        var interest = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("Expense", interest.GetProperty("direction").GetString());

        var refund = await user.Client.PostAsJsonAsync("/api/v1/quick-entry/transactions", new
        {
            amount = 2.40m,
            financialAccountId = card,
            cardMovementType = "Refund",
            description = "Devolución",
        });
        await refund.EnsureOkAsync();
        Assert.Equal("Income", (await refund.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("direction").GetString());
        Assert.Equal(10m, Decimal((await CardAsync(user, card)).GetProperty("card"), "currentDebt"));

        // A payment is never written as a loose movement: it has its own two-leg flow.
        var payment = await user.Client.PostAsJsonAsync("/api/v1/quick-entry/transactions", new
        {
            amount = 5m,
            financialAccountId = card,
            cardMovementType = "Payment",
        });
        Assert.Equal(HttpStatusCode.BadRequest, payment.StatusCode);
    }
}
