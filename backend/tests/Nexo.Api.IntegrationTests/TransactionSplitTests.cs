using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Infrastructure.Persistence;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Movimientos divididos, end to end through the real API. The acceptance case:
/// "TRANSF DIRECTA CHILAN -$220" divided into Esposa $70 + Comida $150 stays ONE
/// movement of -$220, the balance drops $220 once, and every per-category figure
/// shows 70 and 150 -- never $440.
/// </summary>
public class TransactionSplitTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string Statement(params (string Date, string Concept, string Reference, decimal? Debit, decimal? Credit)[] rows) =>
        "Banco Pichincha\nFecha,Concepto,Documento,Debito,Credito\n"
        + string.Join("\n", rows.Select(r => $"{r.Date},{r.Concept},{r.Reference},{r.Debit?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? ""},{r.Credit?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? ""}"));

    private static async Task<Guid> CategoryAsync(TestUser user, string code)
    {
        var categories = await user.GetJsonAsync("/api/v1/categories");
        return categories.EnumerateArray().First(c => c.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> OwnCategoryAsync(TestUser user, string name, bool isIncome = false)
    {
        var response = await user.Client.PostAsJsonAsync("/api/v1/categories", new { name, icon = "people", color = "#D8665B", isIncome });
        await response.EnsureOkAsync();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
    }

    private sealed record Setup(TestUser User, Guid Account, Guid Movement, Guid Esposa, Guid Comida);

    /// <summary>An imported -$220 "TRANSF DIRECTA CHILAN", categorised as Comida.</summary>
    private async Task<Setup> ChilanAsync()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: null);
        var items = await user.ConfirmAndListAsync(account, Statement(("05/03/2026", "TRANSF DIRECTA CHILAN", "DOC-4821", 220m, null)));
        var movement = items[0].GetProperty("id").GetGuid();

        var comida = await CategoryAsync(user, "COMIDA");
        var esposa = await OwnCategoryAsync(user, "Esposa");

        var categorize = await user.Client.PutAsJsonAsync($"/api/v1/transactions/{movement}/category", new { categoryId = comida, createRule = false });
        await categorize.EnsureOkAsync();

        return new Setup(user, account, movement, esposa, comida);
    }

    private static async Task<HttpResponseMessage> SplitAsync(TestUser user, Guid movement, object body) =>
        await user.Client.PutAsJsonAsync($"/api/v1/transactions/{movement}/splits", body);

    private static async Task<JsonElement> SplitOkAsync(TestUser user, Guid movement, object body)
    {
        var response = await SplitAsync(user, movement, body);
        await response.EnsureOkAsync();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    private static object EsposaComida(Setup s, decimal esposa = 70m, decimal comida = 150m) =>
        new { splits = new object[] { new { categoryId = s.Esposa, amount = esposa, note = "Transferencia esposa" }, new { categoryId = s.Comida, amount = comida } } };

    private static decimal CategoryTotal(JsonElement breakdown, Guid categoryId) =>
        breakdown.EnumerateArray()
            .Where(c => c.GetProperty("categoryId").GetGuid() == categoryId)
            .Select(c => c.GetProperty("total").GetDecimal())
            .SingleOrDefault();

    // ------------------------------------------------------------------ cases

    [Fact]
    public async Task The_acceptance_case_keeps_one_220_movement_and_never_counts_440()
    {
        var s = await ChilanAsync();
        var balanceBefore = (await s.User.GetJsonAsync("/api/v1/accounts"))[0].GetProperty("balance").GetDecimal();
        var committedBefore = await s.User.GetJsonAsync("/api/v1/finance/committed");

        var detail = await SplitOkAsync(s.User, s.Movement, EsposaComida(s));

        // One movement, untouched bank data.
        Assert.True(detail.GetProperty("isSplit").GetBoolean());
        Assert.Equal(-220m, detail.GetProperty("signedAmount").GetDecimal());
        Assert.Equal("TRANSF DIRECTA CHILAN", detail.GetProperty("description").GetString());
        Assert.Equal("DOC-4821", detail.GetProperty("externalReference").GetString());
        Assert.NotEqual(JsonValueKind.Null, detail.GetProperty("importId").ValueKind);
        Assert.Equal("ManualSplit", detail.GetProperty("categorySource").GetString());
        var parts = detail.GetProperty("splits").EnumerateArray().ToList();
        Assert.Equal(2, parts.Count);
        Assert.Equal(220m, parts.Sum(p => p.GetProperty("amount").GetDecimal()));
        Assert.Equal("Transferencia esposa", parts[0].GetProperty("note").GetString());

        var list = await s.User.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, list.GetProperty("items")[0].GetProperty("splits").GetArrayLength());

        // Balance and Disponible: exactly as before.
        Assert.Equal(balanceBefore, (await s.User.GetJsonAsync("/api/v1/accounts"))[0].GetProperty("balance").GetDecimal());
        Assert.Equal(-220m, balanceBefore);
        var committedAfter = await s.User.GetJsonAsync("/api/v1/finance/committed");
        Assert.Equal(committedBefore.GetProperty("currentMoney").GetDecimal(), committedAfter.GetProperty("currentMoney").GetDecimal());
        Assert.Equal(committedBefore.GetProperty("available").GetDecimal(), committedAfter.GetProperty("available").GetDecimal());

        // Home: total 220, Esposa 70, Comida 150.
        var summary = await s.User.GetJsonAsync("/api/v1/summary");
        Assert.Equal(220m, summary.GetProperty("month").GetProperty("expense").GetDecimal());
        var breakdown = summary.GetProperty("categoryBreakdown");
        Assert.Equal(70m, CategoryTotal(breakdown, s.Esposa));
        Assert.Equal(150m, CategoryTotal(breakdown, s.Comida));
        Assert.Equal(220m, breakdown.EnumerateArray().Sum(c => c.GetProperty("total").GetDecimal()));

        // Estadísticas (March is the suite's current month).
        var dashboard = await s.User.GetJsonAsync("/api/v1/analytics/dashboard?period=month");
        Assert.Equal(220m, dashboard.GetProperty("kpis").GetProperty("expense").GetDecimal());
        var trend = dashboard.GetProperty("categoryBreakdown");
        Assert.Equal(70m, CategoryTotal(trend, s.Esposa));
        Assert.Equal(150m, CategoryTotal(trend, s.Comida));
        Assert.Equal(220m, trend.EnumerateArray().Sum(c => c.GetProperty("total").GetDecimal()));
    }

    [Fact]
    public async Task Filtering_by_either_category_returns_the_movement_once()
    {
        var s = await ChilanAsync();
        await SplitOkAsync(s.User, s.Movement, EsposaComida(s));

        var byComida = await s.User.GetJsonAsync($"/api/v1/transactions?categoryId={s.Comida}");
        var byEsposa = await s.User.GetJsonAsync($"/api/v1/transactions?categoryId={s.Esposa}");

        Assert.Equal(1, byComida.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, byEsposa.GetProperty("totalCount").GetInt32());
        Assert.Equal(s.Movement, byEsposa.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Budgets_consume_only_their_part()
    {
        var s = await ChilanAsync();
        await SplitOkAsync(s.User, s.Movement, EsposaComida(s));

        var comida = await s.User.Client.PostAsJsonAsync("/api/v1/budgets", new { categoryId = s.Comida, amount = 300m });
        var esposa = await s.User.Client.PostAsJsonAsync("/api/v1/budgets", new { categoryId = s.Esposa, amount = 100m, reserveFunds = true });
        var comidaBudget = (await (await comida.EnsureOkAsync()).Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("budget");
        var esposaBudget = (await (await esposa.EnsureOkAsync()).Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("budget");

        Assert.Equal(150m, comidaBudget.GetProperty("progress").GetProperty("spent").GetDecimal());
        Assert.Equal(70m, esposaBudget.GetProperty("progress").GetProperty("spent").GetDecimal());

        // The reserved Esposa budget reserves what is left of it (30), not something derived from $220.
        Assert.Equal(30m, (await s.User.GetJsonAsync("/api/v1/finance/committed")).GetProperty("committed").GetDecimal());

        var movements = await s.User.GetJsonAsync($"/api/v1/budgets/{comidaBudget.GetProperty("id").GetGuid()}/movements");
        Assert.Single(movements.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Incomplete_or_excessive_divisions_are_rejected_and_nothing_changes()
    {
        var s = await ChilanAsync();

        var incomplete = await SplitAsync(s.User, s.Movement, EsposaComida(s, 70m, 100m));
        Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
        Assert.Contains("Falta asignar $50.00", await incomplete.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var excessive = await SplitAsync(s.User, s.Movement, EsposaComida(s, 100m, 150m));
        Assert.Equal(HttpStatusCode.BadRequest, excessive.StatusCode);
        Assert.Contains("Te pasaste por $30.00", await excessive.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var zero = await SplitAsync(s.User, s.Movement, EsposaComida(s, 0m, 220m));
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);

        var detail = await s.User.GetJsonAsync($"/api/v1/transactions/{s.Movement}");
        Assert.False(detail.GetProperty("isSplit").GetBoolean());
        Assert.Equal(s.Comida, detail.GetProperty("categoryId").GetGuid());
    }

    [Fact]
    public async Task Leftover_can_go_to_sin_categoria_and_decimals_are_exact()
    {
        var s = await ChilanAsync();

        var detail = await SplitOkAsync(s.User, s.Movement, new
        {
            splits = new object[]
            {
                new { categoryId = s.Esposa, amount = 70.33m },
                new { categoryId = s.Comida, amount = 99.67m },
                new { categoryId = (Guid?)null, amount = 50m },
            },
        });

        Assert.Equal(220m, detail.GetProperty("splits").EnumerateArray().Sum(p => p.GetProperty("amount").GetDecimal()));

        var breakdown = (await s.User.GetJsonAsync("/api/v1/summary")).GetProperty("categoryBreakdown");
        Assert.Equal(70.33m, CategoryTotal(breakdown, s.Esposa));
        Assert.Equal(99.67m, CategoryTotal(breakdown, s.Comida));
        Assert.Equal(50m, CategoryTotal(breakdown, Guid.Empty));
    }

    [Fact]
    public async Task Income_divides_too()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: null);
        var items = await user.ConfirmAndListAsync(account, Statement(("06/03/2026", "ACREDITACION PAGOS", "DOC-1", null, 1000m)));
        var movement = items[0].GetProperty("id").GetGuid();

        var salario = await CategoryAsync(user, "INGRESOS");
        var freelance = await OwnCategoryAsync(user, "Freelance", isIncome: true);

        var detail = await SplitOkAsync(user, movement, new
        {
            splits = new object[]
            {
                new { categoryId = salario, amount = 800m },
                new { categoryId = freelance, amount = 150m },
                new { categoryId = (Guid?)null, amount = 50m },
            },
        });

        Assert.Equal(1000m, detail.GetProperty("signedAmount").GetDecimal());
        Assert.Equal(3, detail.GetProperty("splits").GetArrayLength());

        var summary = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(1000m, summary.GetProperty("month").GetProperty("income").GetDecimal());
        Assert.Equal(0m, summary.GetProperty("month").GetProperty("expense").GetDecimal());
        Assert.Empty(summary.GetProperty("categoryBreakdown").EnumerateArray());

        var byFreelance = await user.GetJsonAsync($"/api/v1/transactions?categoryId={freelance}");
        Assert.Equal(1, byFreelance.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Editing_a_split_and_changing_its_category_replace_the_division()
    {
        var s = await ChilanAsync();
        await SplitOkAsync(s.User, s.Movement, EsposaComida(s));

        var transporte = await CategoryAsync(s.User, "TRANSPORTE");
        await SplitOkAsync(s.User, s.Movement, new
        {
            splits = new object[] { new { categoryId = s.Esposa, amount = 20m }, new { categoryId = transporte, amount = 200m } },
        });

        var breakdown = (await s.User.GetJsonAsync("/api/v1/summary")).GetProperty("categoryBreakdown");
        Assert.Equal(20m, CategoryTotal(breakdown, s.Esposa));
        Assert.Equal(200m, CategoryTotal(breakdown, transporte));
        Assert.Equal(0m, CategoryTotal(breakdown, s.Comida));

        var splits = await s.User.GetJsonAsync($"/api/v1/transactions/{s.Movement}/splits");
        Assert.Equal(2, splits.GetProperty("splits").GetArrayLength());
        Assert.Equal(2, splits.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Removing_the_division_restores_one_category_without_touching_the_movement()
    {
        var s = await ChilanAsync();
        await SplitOkAsync(s.User, s.Movement, EsposaComida(s));

        var response = await s.User.Client.DeleteAsync($"/api/v1/transactions/{s.Movement}/splits?categoryId={s.Esposa}");
        await response.EnsureOkAsync();
        var detail = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

        Assert.False(detail.GetProperty("isSplit").GetBoolean());
        Assert.Equal(s.Esposa, detail.GetProperty("categoryId").GetGuid());
        Assert.Empty(detail.GetProperty("splits").EnumerateArray());
        Assert.Equal(-220m, detail.GetProperty("signedAmount").GetDecimal());

        var breakdown = (await s.User.GetJsonAsync("/api/v1/summary")).GetProperty("categoryBreakdown");
        Assert.Equal(220m, CategoryTotal(breakdown, s.Esposa));
    }

    [Fact]
    public async Task A_single_category_change_cannot_silently_destroy_a_division()
    {
        var s = await ChilanAsync();
        await SplitOkAsync(s.User, s.Movement, EsposaComida(s));

        var change = await s.User.Client.PutAsJsonAsync($"/api/v1/transactions/{s.Movement}/category", new { categoryId = s.Comida, createRule = false });
        Assert.Equal(HttpStatusCode.Conflict, change.StatusCode);
        Assert.True((await s.User.GetJsonAsync($"/api/v1/transactions/{s.Movement}")).GetProperty("isSplit").GetBoolean());
    }

    [Fact]
    public async Task A_confirmed_internal_transfer_stays_out_of_categories_even_if_divided()
    {
        var user = await factory.RegisterUserAsync();
        var checking = await user.CreateAccountAsync(openingBalance: null);
        var savings = await user.CreateAccountAsync(openingBalance: null);
        var outgoing = (await user.ConfirmAndListAsync(checking, Statement(("05/03/2026", "TRANSFERENCIA A AHORROS", "TRX-1", 300m, null)))
            )[0].GetProperty("id").GetGuid();
        var incoming = (await user.ConfirmAndListAsync(savings, Statement(("06/03/2026", "TRANSFERENCIA DESDE CORRIENTE", "TRX-2", null, 300m))))
            .EnumerateArray().First(t => t.GetProperty("direction").GetString() == "Income").GetProperty("id").GetGuid();

        var comida = await CategoryAsync(user, "COMIDA");
        var otros = await CategoryAsync(user, "OTROS");
        await SplitOkAsync(user, outgoing, new { splits = new object[] { new { categoryId = comida, amount = 100m }, new { categoryId = otros, amount = 200m } } });
        Assert.Equal(100m, CategoryTotal((await user.GetJsonAsync("/api/v1/summary")).GetProperty("categoryBreakdown"), comida));

        var confirm = await user.Client.PostAsJsonAsync("/api/v1/transfers/confirm", new { outgoingTransactionId = outgoing, incomingTransactionId = incoming });
        await confirm.EnsureOkAsync();

        var summary = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(0m, summary.GetProperty("month").GetProperty("expense").GetDecimal());
        Assert.Empty(summary.GetProperty("categoryBreakdown").EnumerateArray());
    }

    [Fact]
    public async Task Deleting_a_divided_movement_deletes_its_splits()
    {
        var user = await factory.RegisterUserAsync();
        var comida = await CategoryAsync(user, "COMIDA");
        var otros = await CategoryAsync(user, "OTROS");
        var created = await user.Client.PostAsJsonAsync("/api/v1/quick-entry/transactions", new { amount = 30m, categoryId = comida, occurredAt = "2026-03-05T15:00:00Z" });
        var id = (await (await created.EnsureOkAsync()).Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        await SplitOkAsync(user, id, new { splits = new object[] { new { categoryId = comida, amount = 10m }, new { categoryId = otros, amount = 20m } } });

        var deleted = await user.Client.DeleteAsync($"/api/v1/quick-entry/transactions/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        Assert.Equal(0, await db.TransactionSplits.IgnoreQueryFilters().CountAsync(sp => sp.TransactionId == id));
    }

    [Fact]
    public async Task A_category_used_by_a_split_cannot_be_deleted_silently()
    {
        var s = await ChilanAsync();
        await SplitOkAsync(s.User, s.Movement, EsposaComida(s));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var esposa = await db.Categories.IgnoreQueryFilters().SingleAsync(c => c.Id == s.Esposa);
        db.Categories.Remove(esposa);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_stale_version_is_rejected_and_concurrent_saves_never_overdistribute()
    {
        var s = await ChilanAsync();
        await SplitOkAsync(s.User, s.Movement, EsposaComida(s));

        var stale = await SplitAsync(s.User, s.Movement, new
        {
            splits = new object[] { new { categoryId = s.Esposa, amount = 110m }, new { categoryId = s.Comida, amount = 110m } },
            expectedVersion = 0,
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        // Two saves racing on the same movement: whatever wins, the stored division adds up to $220.
        var first = SplitAsync(s.User, s.Movement, EsposaComida(s, 20m, 200m));
        var second = SplitAsync(s.User, s.Movement, EsposaComida(s, 120m, 100m));
        var results = await Task.WhenAll(first, second);
        Assert.Contains(results, r => r.IsSuccessStatusCode);
        Assert.All(results, r => Assert.True(r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.Conflict));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var stored = await db.TransactionSplits.IgnoreQueryFilters().Where(sp => sp.TransactionId == s.Movement).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.Equal(220m, stored.Sum(sp => sp.Amount));
    }

    [Fact]
    public async Task Movements_without_splits_behave_exactly_as_before()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: null);
        var items = await user.ConfirmAndListAsync(account, Statement(("05/03/2026", "SUPERMAXI", "DOC-9", 42m, null)));
        var id = items[0].GetProperty("id").GetGuid();
        var comida = await CategoryAsync(user, "COMIDA");
        await (await user.Client.PutAsJsonAsync($"/api/v1/transactions/{id}/category", new { categoryId = comida, createRule = false })).EnsureOkAsync();

        var detail = await user.GetJsonAsync($"/api/v1/transactions/{id}");
        Assert.False(detail.GetProperty("isSplit").GetBoolean());
        Assert.Empty(detail.GetProperty("splits").EnumerateArray());
        Assert.Equal(42m, CategoryTotal((await user.GetJsonAsync("/api/v1/summary")).GetProperty("categoryBreakdown"), comida));

        var splits = await user.GetJsonAsync($"/api/v1/transactions/{id}/splits");
        Assert.False(splits.GetProperty("isSplit").GetBoolean());

        var notSplit = await user.Client.DeleteAsync($"/api/v1/transactions/{id}/splits");
        Assert.Equal(HttpStatusCode.Conflict, notSplit.StatusCode);
    }

    [Fact]
    public async Task Another_users_movement_cannot_be_divided()
    {
        var s = await ChilanAsync();
        var intruder = await factory.RegisterUserAsync();

        var response = await SplitAsync(intruder, s.Movement, EsposaComida(s));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Insights_see_the_parts_not_the_whole_movement()
    {
        var s = await ChilanAsync();
        await SplitOkAsync(s.User, s.Movement, EsposaComida(s));

        var insights = await s.User.PostForJsonAsync("/api/v1/insights/refresh");
        var top = insights.EnumerateArray().Single(i => i.GetProperty("code").GetString() == "TOP_CATEGORY");

        Assert.Equal(150m, top.GetProperty("value").GetDecimal());
        Assert.Contains("Comida", top.GetProperty("title").GetString(), StringComparison.Ordinal);
    }
}
