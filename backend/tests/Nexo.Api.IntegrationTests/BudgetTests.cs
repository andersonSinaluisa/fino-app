using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Presupuestos + Comprometido, end to end through the real API.
///
/// The suite clock is 2026-03-10 17:00 UTC = 12:00 in Guayaquil, so "this month"
/// is March 2026. Movements are dated BEFORE the account was opened-and-anchored
/// only when a test needs Tu dinero to stay exactly at the opening balance;
/// otherwise the assertions are relative to the current money the API reports.
/// </summary>
public class BudgetTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------------------------------------------------------------- helpers

    private static async Task<Guid> CategoryAsync(TestUser user, string code)
    {
        var categories = await user.GetJsonAsync("/api/v1/categories");
        return categories.EnumerateArray().First(c => c.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> MovementAsync(
        TestUser user,
        Guid accountId,
        decimal amount,
        Guid? categoryId,
        string occurredAt,
        string direction = "Expense",
        string? description = null)
    {
        var response = await user.Client.PostAsJsonAsync("/api/v1/quick-entry/transactions", new
        {
            amount,
            direction,
            financialAccountId = accountId,
            categoryId,
            leaveUncategorized = categoryId is null,
            occurredAt = DateTimeOffset.Parse(occurredAt, System.Globalization.CultureInfo.InvariantCulture),
            description,
        });
        await response.EnsureOkAsync();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return created.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> CreateBudgetAsync(TestUser user, object body)
    {
        var response = await user.Client.PostAsJsonAsync("/api/v1/budgets", body);
        await response.EnsureOkAsync();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("budget");
    }

    private static async Task<JsonElement> UpdateBudgetAsync(TestUser user, Guid id, object body)
    {
        var response = await user.Client.PutAsJsonAsync($"/api/v1/budgets/{id}", body);
        await response.EnsureOkAsync();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("budget");
    }

    private static decimal Dec(JsonElement element, string property) => element.GetProperty(property).GetDecimal();

    private static JsonElement Progress(JsonElement budget) => budget.GetProperty("progress");

    private static async Task<JsonElement> BudgetAsync(TestUser user, Guid id, string? date = null) =>
        (await user.GetJsonAsync($"/api/v1/budgets/{id}" + (date is null ? string.Empty : $"?date={date}"))).GetProperty("budget");

    private static Guid Id(JsonElement budget) => budget.GetProperty("id").GetGuid();

    // ------------------------------------------------------------------ cases

    [Fact]
    public async Task A_limit_budget_tracks_spending_but_never_touches_committed()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: 668.89m);
        var food = await CategoryAsync(user, "COMIDA");

        await MovementAsync(user, account, 42m, food, "2026-03-05T15:00:00Z", description: "Supermercado");
        await MovementAsync(user, account, 78m, food, "2026-03-06T15:00:00Z", description: "Restaurante");

        var budget = await CreateBudgetAsync(user, new { categoryId = food, amount = 300m, reserveFunds = false });

        Assert.Equal("Comida", budget.GetProperty("name").GetString());
        Assert.Equal("Monthly", budget.GetProperty("period").GetString());
        Assert.Equal("2026-03-01", budget.GetProperty("window").GetProperty("start").GetString());
        Assert.Equal("2026-03-31", budget.GetProperty("window").GetProperty("end").GetString());
        Assert.Equal(120m, Dec(Progress(budget), "spent"));
        Assert.Equal(180m, Dec(Progress(budget), "remaining"));
        Assert.Equal(40m, Dec(Progress(budget), "percentUsed"));
        Assert.Equal("Normal", Progress(budget).GetProperty("level").GetString());
        Assert.Equal(0m, Dec(Progress(budget), "reserved"));

        var committed = await user.GetJsonAsync("/api/v1/finance/committed");
        Assert.Equal(0m, Dec(committed, "committed"));
        Assert.Equal(Dec(committed, "currentMoney"), Dec(committed, "available"));
    }

    [Fact]
    public async Task A_reserving_budget_feeds_committed_and_the_preview_uses_the_same_engine()
    {
        var user = await factory.RegisterUserAsync();
        await user.CreateAccountAsync(openingBalance: 668.89m);
        var utilities = await CategoryAsync(user, "SERVICIOS");

        var before = await user.GetJsonAsync("/api/v1/finance/committed");
        var money = Dec(before, "currentMoney");

        var previewResponse = await user.Client.PostAsJsonAsync("/api/v1/budgets/preview", new { categoryId = utilities, amount = 250m, reserveFunds = true });
        var preview = await (await previewResponse.EnsureOkAsync()).Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(250m, Dec(preview, "budgetContribution"));
        Assert.Equal(money - 250m, Dec(preview, "availableAfter"));

        var budget = await CreateBudgetAsync(user, new { categoryId = utilities, name = "Alquiler", amount = 250m, reserveFunds = true, priority = "Essential" });
        Assert.Equal(250m, Dec(Progress(budget), "reserved"));

        var after = await user.GetJsonAsync("/api/v1/finance/committed");
        Assert.Equal(250m, Dec(after, "committed"));
        Assert.Equal(Dec(preview, "availableAfter"), Dec(after, "available"));

        var source = Assert.Single(after.GetProperty("sources").EnumerateArray());
        Assert.Equal("reserved_budget", source.GetProperty("type").GetString());
        var item = Assert.Single(source.GetProperty("items").EnumerateArray());
        Assert.Equal(Id(budget), item.GetProperty("budgetId").GetGuid());
        Assert.Equal("Alquiler", item.GetProperty("label").GetString());
    }

    [Fact]
    public async Task Paying_the_reserved_bill_releases_it_and_editing_or_deleting_the_payment_brings_it_back()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: 500m);
        var utilities = await CategoryAsync(user, "SERVICIOS");
        await CreateBudgetAsync(user, new { categoryId = utilities, name = "Internet", amount = 35m, reserveFunds = true });

        Assert.Equal(35m, Dec(await user.GetJsonAsync("/api/v1/finance/committed"), "committed"));

        var payment = await MovementAsync(user, account, 35m, utilities, "2026-03-08T15:00:00Z", description: "CNT Internet");
        Assert.Equal(0m, Dec(await user.GetJsonAsync("/api/v1/finance/committed"), "committed"));

        // Edit the movement: half of it moves to another category.
        var food = await CategoryAsync(user, "COMIDA");
        var edit = await user.Client.PutAsJsonAsync($"/api/v1/quick-entry/transactions/{payment}", new { amount = 35m, categoryId = food });
        await edit.EnsureOkAsync();
        Assert.Equal(35m, Dec(await user.GetJsonAsync("/api/v1/finance/committed"), "committed"));

        var back = await user.Client.PutAsJsonAsync($"/api/v1/quick-entry/transactions/{payment}", new { amount = 20m, categoryId = utilities });
        await back.EnsureOkAsync();
        Assert.Equal(15m, Dec(await user.GetJsonAsync("/api/v1/finance/committed"), "committed"));

        var deleted = await user.Client.DeleteAsync($"/api/v1/quick-entry/transactions/{payment}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(35m, Dec(await user.GetJsonAsync("/api/v1/finance/committed"), "committed"));
    }

    [Fact]
    public async Task Editing_amount_and_reserve_updates_committed_and_keeps_history()
    {
        var user = await factory.RegisterUserAsync();
        await user.CreateAccountAsync(openingBalance: 1000m);
        var food = await CategoryAsync(user, "COMIDA");

        var budget = await CreateBudgetAsync(user, new { categoryId = food, amount = 300m, startDate = "2026-02-01", reserveFunds = false });
        var id = Id(budget);

        var edited = await UpdateBudgetAsync(user, id, new { categoryId = food, amount = 350m, reserveFunds = true, isActive = true });
        Assert.Equal(350m, Dec(Progress(edited), "amount"));
        Assert.Equal(350m, Dec(await user.GetJsonAsync("/api/v1/finance/committed"), "committed"));

        // February keeps the amount it had.
        var february = await BudgetAsync(user, id, "2026-02-15");
        Assert.Equal(300m, Dec(Progress(february), "amount"));
        Assert.Equal(0m, Dec(Progress(february), "reserved"));

        var detail = await user.GetJsonAsync($"/api/v1/budgets/{id}");
        Assert.Equal(2, detail.GetProperty("history").GetArrayLength());

        await UpdateBudgetAsync(user, id, new { categoryId = food, amount = 350m, reserveFunds = false, isActive = true });
        Assert.Equal(0m, Dec(await user.GetJsonAsync("/api/v1/finance/committed"), "committed"));
    }

    [Fact]
    public async Task Pausing_stops_the_reserve_and_deleting_removes_the_budget_but_not_the_movements()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: 1000m);
        var food = await CategoryAsync(user, "COMIDA");
        await MovementAsync(user, account, 10m, food, "2026-03-02T15:00:00Z");

        var budget = await CreateBudgetAsync(user, new { categoryId = food, amount = 100m, reserveFunds = true });
        var id = Id(budget);
        Assert.Equal(90m, Dec(await user.GetJsonAsync("/api/v1/finance/committed"), "committed"));

        var paused = await UpdateBudgetAsync(user, id, new { categoryId = food, amount = 100m, reserveFunds = true, isActive = false });
        Assert.False(paused.GetProperty("isActive").GetBoolean());
        Assert.Equal(0m, Dec(await user.GetJsonAsync("/api/v1/finance/committed"), "committed"));

        var deleted = await user.Client.DeleteAsync($"/api/v1/budgets/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/api/v1/budgets/{id}")).StatusCode);

        var list = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Movements_outside_the_period_do_not_count_and_a_new_month_starts_from_zero()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: 1000m);
        var food = await CategoryAsync(user, "COMIDA");

        await MovementAsync(user, account, 50m, food, "2026-02-20T15:00:00Z");
        await MovementAsync(user, account, 30m, food, "2026-03-03T15:00:00Z");

        var budget = await CreateBudgetAsync(user, new { categoryId = food, amount = 300m, startDate = "2026-02-01" });
        var id = Id(budget);
        Assert.Equal(30m, Dec(Progress(budget), "spent"));

        var february = await BudgetAsync(user, id, "2026-02-10");
        Assert.Equal(50m, Dec(Progress(february), "spent"));
        Assert.False(Progress(february).GetProperty("isCurrentWindow").GetBoolean());

        var april = await user.GetJsonAsync("/api/v1/budgets?date=2026-04-05");
        var aprilBudget = Assert.Single(april.GetProperty("budgets").EnumerateArray());
        Assert.Equal("2026-04-01", aprilBudget.GetProperty("window").GetProperty("start").GetString());
        Assert.Equal(0m, Dec(Progress(aprilBudget), "spent"));
        Assert.Equal("Abril 2026", april.GetProperty("label").GetString());

        var movements = await user.GetJsonAsync($"/api/v1/budgets/{id}/movements");
        Assert.Single(movements.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task An_exceeded_budget_says_so_and_wins_the_home_insight()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: 1000m);
        var food = await CategoryAsync(user, "COMIDA");
        await MovementAsync(user, account, 124m, food, "2026-03-04T15:00:00Z");

        var budget = await CreateBudgetAsync(user, new { categoryId = food, amount = 100m });
        Assert.Equal("Exceeded", Progress(budget).GetProperty("level").GetString());
        Assert.Equal(24m, Dec(Progress(budget), "overspent"));
        Assert.Equal(0m, Dec(Progress(budget), "remaining"));

        var overview = await user.GetJsonAsync("/api/v1/budgets");
        Assert.Equal(1, overview.GetProperty("totals").GetProperty("exceededCount").GetInt32());
        var insight = overview.GetProperty("topInsight");
        Assert.Equal("Exceeded", insight.GetProperty("kind").GetString());
        Assert.Equal(Id(budget), insight.GetProperty("budgetId").GetGuid());
    }

    [Fact]
    public async Task Committed_above_the_balance_is_reported_as_overcommitted()
    {
        var user = await factory.RegisterUserAsync();
        await user.CreateAccountAsync(openingBalance: 100m);
        var utilities = await CategoryAsync(user, "SERVICIOS");

        await CreateBudgetAsync(user, new { categoryId = utilities, name = "Alquiler", amount = 150m, reserveFunds = true });

        var committed = await user.GetJsonAsync("/api/v1/finance/committed");
        Assert.Equal(100m, Dec(committed, "currentMoney"));
        Assert.Equal(150m, Dec(committed, "committed"));
        Assert.Equal(0m, Dec(committed, "available"));
        Assert.Equal(50m, Dec(committed, "overcommitted"));
        Assert.True(committed.GetProperty("isOvercommitted").GetBoolean());
    }

    [Fact]
    public async Task Spending_from_several_accounts_counts_and_current_money_adds_every_account()
    {
        var user = await factory.RegisterUserAsync();
        var first = await user.CreateAccountAsync(openingBalance: 300m);
        var second = await user.CreateAccountAsync(openingBalance: 200m);
        var transport = await CategoryAsync(user, "TRANSPORTE");

        await MovementAsync(user, first, 20m, transport, "2026-03-02T15:00:00Z");
        await MovementAsync(user, second, 15m, transport, "2026-03-03T15:00:00Z");

        var budget = await CreateBudgetAsync(user, new { categoryId = transport, amount = 150m, reserveFunds = true });
        Assert.Equal(35m, Dec(Progress(budget), "spent"));

        var committed = await user.GetJsonAsync("/api/v1/finance/committed");
        Assert.Equal(500m, Dec(committed, "currentMoney"));
        Assert.Equal(115m, Dec(committed, "committed"));
        Assert.Equal(385m, Dec(committed, "available"));
    }

    [Fact]
    public async Task A_confirmed_internal_transfer_is_not_budget_spending()
    {
        var user = await factory.RegisterUserAsync();
        var checking = await user.CreateAccountAsync(openingBalance: 1000m);
        var savings = await user.CreateAccountAsync(openingBalance: 0m);
        var transfers = await CategoryAsync(user, "TRANSFERENCIAS");

        var budget = await CreateBudgetAsync(user, new { amount = 500m });
        Assert.Null(budget.GetProperty("categoryId").GetString());

        var outgoing = await MovementAsync(user, checking, 300m, transfers, "2026-03-05T15:00:00Z", description: "Transferencia a ahorros");
        var incoming = await MovementAsync(user, savings, 300m, transfers, "2026-03-05T15:05:00Z", direction: "Income", description: "Transferencia desde corriente");

        Assert.Equal(300m, Dec(Progress(await BudgetAsync(user, Id(budget))), "spent"));

        var confirm = await user.Client.PostAsJsonAsync("/api/v1/transfers/confirm", new { outgoingTransactionId = outgoing, incomingTransactionId = incoming });
        await confirm.EnsureOkAsync();

        Assert.Equal(0m, Dec(Progress(await BudgetAsync(user, Id(budget))), "spent"));
    }

    [Fact]
    public async Task A_refund_in_the_same_category_gives_budget_back()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: 1000m);
        var shopping = await CategoryAsync(user, "COMPRAS");

        await MovementAsync(user, account, 80m, shopping, "2026-03-02T15:00:00Z");
        await MovementAsync(user, account, 30m, shopping, "2026-03-04T15:00:00Z", direction: "Income", description: "Devolución");

        var budget = await CreateBudgetAsync(user, new { categoryId = shopping, amount = 200m });
        Assert.Equal(50m, Dec(Progress(budget), "spent"));

        var movements = await user.GetJsonAsync($"/api/v1/budgets/{Id(budget)}/movements");
        Assert.Equal(2, movements.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task The_general_budget_only_takes_what_no_category_budget_tracks()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: 1000m);
        var food = await CategoryAsync(user, "COMIDA");
        var transport = await CategoryAsync(user, "TRANSPORTE");

        await MovementAsync(user, account, 40m, food, "2026-03-02T15:00:00Z");
        await MovementAsync(user, account, 25m, transport, "2026-03-03T15:00:00Z");
        await MovementAsync(user, account, 5m, null, "2026-03-03T16:00:00Z");
        await MovementAsync(user, account, 900m, null, "2026-03-01T15:00:00Z", direction: "Income", description: "Sueldo");

        await CreateBudgetAsync(user, new { categoryId = food, amount = 300m });
        var general = await CreateBudgetAsync(user, new { amount = 200m });

        Assert.Equal("Presupuesto general", general.GetProperty("name").GetString());
        Assert.Equal(30m, Dec(Progress(general), "spent"));
    }

    [Fact]
    public async Task Two_active_budgets_cannot_consume_the_same_category_on_the_same_dates()
    {
        var user = await factory.RegisterUserAsync();
        await user.CreateAccountAsync(openingBalance: 1000m);
        var food = await CategoryAsync(user, "COMIDA");

        var first = await CreateBudgetAsync(user, new { categoryId = food, amount = 300m });

        var sameMonth = await user.Client.PostAsJsonAsync("/api/v1/budgets", new { categoryId = food, amount = 100m });
        Assert.Equal(HttpStatusCode.Conflict, sameMonth.StatusCode);

        var weekly = await user.Client.PostAsJsonAsync("/api/v1/budgets", new { categoryId = food, amount = 70m, period = "Weekly" });
        Assert.Equal(HttpStatusCode.Conflict, weekly.StatusCode);

        // A second general budget is just as ambiguous.
        await CreateBudgetAsync(user, new { amount = 500m });
        Assert.Equal(HttpStatusCode.Conflict, (await user.Client.PostAsJsonAsync("/api/v1/budgets", new { amount = 50m })).StatusCode);

        // Pausing the first one frees the category.
        await UpdateBudgetAsync(user, Id(first), new { categoryId = food, amount = 300m, reserveFunds = false, isActive = false });
        var afterPause = await user.Client.PostAsJsonAsync("/api/v1/budgets", new { categoryId = food, amount = 70m, period = "Weekly" });
        await afterPause.EnsureOkAsync();

        // ...and resuming it now conflicts with the weekly one.
        var resume = await user.Client.PutAsJsonAsync($"/api/v1/budgets/{Id(first)}", new { categoryId = food, amount = 300m, reserveFunds = false, isActive = true });
        Assert.Equal(HttpStatusCode.Conflict, resume.StatusCode);
    }

    [Fact]
    public async Task A_recurring_payment_covered_by_a_reserved_budget_is_not_counted_twice()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: 500m);
        var utilities = await CategoryAsync(user, "SERVICIOS");

        // "CNT Internet" every month -> recurring. Not paid yet in March.
        await MovementAsync(user, account, 35m, utilities, "2026-01-09T15:00:00Z", description: "CNT Internet");
        await MovementAsync(user, account, 35m, utilities, "2026-02-09T15:00:00Z", description: "CNT Internet");

        var before = await user.GetJsonAsync("/api/v1/finance/committed");
        Assert.Equal(35m, Dec(before, "committed"));
        var upcoming = Assert.Single(before.GetProperty("sources").EnumerateArray());
        Assert.Equal("upcoming_payment", upcoming.GetProperty("type").GetString());

        await CreateBudgetAsync(user, new { categoryId = utilities, name = "Internet", amount = 35m, reserveFunds = true });

        var after = await user.GetJsonAsync("/api/v1/finance/committed");
        Assert.Equal(35m, Dec(after, "committed"));

        var upcomingAfter = after.GetProperty("sources").EnumerateArray().First(s => s.GetProperty("type").GetString() == "upcoming_payment");
        var item = Assert.Single(upcomingAfter.GetProperty("items").EnumerateArray());
        Assert.Equal(0m, Dec(item, "amount"));
        Assert.Equal(35m, Dec(item, "grossAmount"));
        Assert.Equal("Internet", item.GetProperty("coveredByBudgetName").GetString());
    }

    [Fact]
    public async Task Budget_dates_follow_the_users_time_zone_not_utc()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: 1000m);
        var food = await CategoryAsync(user, "COMIDA");

        // 2026-03-01 03:00 UTC is still 28 feb, 22:00 in Guayaquil: a February dinner.
        await MovementAsync(user, account, 20m, food, "2026-03-01T03:00:00Z");
        // 2026-03-01 05:30 UTC is 1 mar, 00:30 in Guayaquil: a March one.
        await MovementAsync(user, account, 7m, food, "2026-03-01T05:30:00Z");

        var budget = await CreateBudgetAsync(user, new { categoryId = food, amount = 300m, startDate = "2026-02-01" });
        Assert.Equal(7m, Dec(Progress(budget), "spent"));
        Assert.Equal(20m, Dec(Progress(await BudgetAsync(user, Id(budget), "2026-02-28")), "spent"));
    }

    [Fact]
    public async Task Cents_are_exact()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: 1000m);
        var food = await CategoryAsync(user, "COMIDA");

        await MovementAsync(user, account, 0.10m, food, "2026-03-02T15:00:00Z");
        await MovementAsync(user, account, 0.20m, food, "2026-03-02T16:00:00Z");

        var budget = await CreateBudgetAsync(user, new { categoryId = food, amount = 0.30m, reserveFunds = true });
        Assert.Equal(0.30m, Dec(Progress(budget), "spent"));
        Assert.Equal(0m, Dec(Progress(budget), "remaining"));
        Assert.Equal("Exceeded", Progress(budget).GetProperty("level").GetString());
        Assert.Equal(0m, Dec(await user.GetJsonAsync("/api/v1/finance/committed"), "committed"));
    }

    [Fact]
    public async Task Invalid_budgets_are_rejected_and_other_users_budgets_are_invisible()
    {
        var owner = await factory.RegisterUserAsync();
        var intruder = await factory.RegisterUserAsync();
        var salary = await CategoryAsync(owner, "INGRESOS");

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync("/api/v1/budgets", new { amount = 0m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync("/api/v1/budgets", new { amount = 10m, categoryId = salary })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync("/api/v1/budgets", new { amount = 10m, period = "Custom" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync("/api/v1/budgets", new { amount = 10m, period = "Yearly" })).StatusCode);

        var budget = await CreateBudgetAsync(owner, new { amount = 100m });
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.Client.GetAsync($"/api/v1/budgets/{Id(budget)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.Client.DeleteAsync($"/api/v1/budgets/{Id(budget)}")).StatusCode);
        Assert.Empty((await intruder.GetJsonAsync("/api/v1/budgets")).GetProperty("budgets").EnumerateArray());
    }
}
