using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Entregable 14 ("Categorización v2"): rules can now match by merchant, provider,
/// direction and amount range, the most specific matching rule wins, and a manual
/// category correction teaches a personal, per-merchant rule for next time.
/// </summary>
public class CategorizationTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private static string StatementWithTwoRows(
        string date1, string concept1, decimal amount1,
        string date2, string concept2, decimal amount2) =>
        $"""
        Banco Pichincha
        Fecha,Concepto,Documento,Debito,Credito
        {date1},{concept1},TRX-{Guid.CreateVersion7():N},{amount1.ToString("0.00")},
        {date2},{concept2},TRX-{Guid.CreateVersion7():N},{amount2.ToString("0.00")},
        """;

    private static async Task<JsonElement> FindCategoryAsync(TestUser user, string code)
    {
        var categories = await user.GetJsonAsync("/api/v1/categories");
        return categories.EnumerateArray().First(c => c.GetProperty("code").GetString() == code);
    }

    private static JsonElement FindByDescription(JsonElement items, string descriptionFragment) =>
        items.EnumerateArray().First(t => t.GetProperty("description").GetString()!.Contains(descriptionFragment));

    [Fact]
    public async Task Uber_eats_is_categorized_as_food_while_a_plain_uber_ride_is_transport()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: null);

        var csv = StatementWithTwoRows(
            "05/03/2026", "UBER EATS RESTAURANT ALBORADA", 8.50m,
            "06/03/2026", "UBER VIAJE CENTRO", 4.20m);

        var items = await user.ConfirmAndListAsync(account, csv);

        var eats = FindByDescription(items, "UBER EATS");
        var ride = FindByDescription(items, "UBER VIAJE");

        // The seeded merchant rule ("UBER EATS" -> Comida) is more specific than
        // the plain description rule ("UBER" -> Transporte), so it wins even
        // though both rules match this movement's text.
        Assert.Equal("Comida", eats.GetProperty("categoryName").GetString());
        Assert.Equal("Transporte", ride.GetProperty("categoryName").GetString());
    }

    [Fact]
    public async Task Correcting_a_merchants_category_teaches_a_rule_that_recategorizes_the_same_users_future_movements()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: null);

        // No seeded rule knows this merchant, so it lands in the fallback bucket.
        var firstCsv = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            05/03/2026,FRUTERIA DON PEPE ALBORADA,TRX-8801,8.50,
            """;
        var firstItems = await user.ConfirmAndListAsync(account, firstCsv);
        var first = FindByDescription(firstItems, "FRUTERIA DON PEPE");
        Assert.Equal("Otros", first.GetProperty("categoryName").GetString());

        var salud = await FindCategoryAsync(user, "SALUD");
        var correction = await user.Client.PutAsJsonAsync(
            $"/api/v1/transactions/{first.GetProperty("id").GetGuid()}/category",
            new { categoryId = salud.GetProperty("id").GetGuid(), createRule = true });
        await correction.EnsureOkAsync();

        // A second, later movement from the same merchant (different date, amount
        // and trailing text -- only the first tokens match) is auto-categorized by
        // the freshly learned rule, without asking again.
        var secondCsv = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            20/03/2026,FRUTERIA DON PEPE CENTRO,TRX-8802,12.75,
            """;
        var secondItems = await user.ConfirmAndListAsync(account, secondCsv);
        var second = secondItems.EnumerateArray()
            .First(t => t.GetProperty("id").GetGuid() != first.GetProperty("id").GetGuid());

        Assert.Equal("Salud", second.GetProperty("categoryName").GetString());
    }

    [Fact]
    public async Task A_learned_merchant_rule_never_affects_a_different_users_movements()
    {
        var owner = await factory.RegisterUserAsync();
        var ownerAccount = await owner.CreateAccountAsync(openingBalance: null);

        var ownerCsv = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            05/03/2026,FRUTERIA DON PEPE ALBORADA,TRX-8901,8.50,
            """;
        var ownerItems = await owner.ConfirmAndListAsync(ownerAccount, ownerCsv);
        var ownerTransaction = FindByDescription(ownerItems, "FRUTERIA DON PEPE");

        var salud = await FindCategoryAsync(owner, "SALUD");
        await (await owner.Client.PutAsJsonAsync(
            $"/api/v1/transactions/{ownerTransaction.GetProperty("id").GetGuid()}/category",
            new { categoryId = salud.GetProperty("id").GetGuid(), createRule = true })).EnsureOkAsync();

        // A second, unrelated user buys from the same merchant -- per-user
        // isolation means the owner's freshly learned rule must not apply here.
        var other = await factory.RegisterUserAsync();
        var otherAccount = await other.CreateAccountAsync(openingBalance: null);
        var otherCsv = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            06/03/2026,FRUTERIA DON PEPE CENTRO,TRX-8902,12.75,
            """;
        var otherItems = await other.ConfirmAndListAsync(otherAccount, otherCsv);
        var otherTransaction = FindByDescription(otherItems, "FRUTERIA DON PEPE");

        Assert.Equal("Otros", otherTransaction.GetProperty("categoryName").GetString());
    }
}
