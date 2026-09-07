using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// "Categorización personal": a user's own manual category corrections become
/// personal rules that categorize similar future movements automatically, are
/// isolated per user, and never silently touch history. Covers the mandatory
/// end-to-end scenarios and security tests from the spec (points 12, 13, 17,
/// 18, 19, 21, 23, 24) that <see cref="CategorizationTests"/> does not.
///
/// Several worked examples in the spec ("UBER", "SUPERMAXI", "NETFLIX") already
/// have a seeded <c>SystemRule</c> (see ReferenceDataSeeder), so a fresh import
/// of those merchants is never "Otros" to begin with -- the "starts uncategorized,
/// then the user teaches a rule" scenarios here use merchants the seed catalog
/// does not know, the same substitution <see cref="CategorizationTests"/> already
/// makes with "FRUTERIA DON PEPE". The NETFLIX scenario (point 24c) is kept
/// literal since it does not depend on starting from "Otros".
/// </summary>
public class CategorizationPersonalizationTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private static string OneRowStatement(string date, string concept, decimal amount, string? doc = null) =>
        $"""
        Banco Pichincha
        Fecha,Concepto,Documento,Debito,Credito
        {date},{concept},{doc ?? $"TRX-{Guid.CreateVersion7():N}"},{amount.ToString("0.00")},
        """;

    private static string TwoRowStatement(
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

    /// <summary>
    /// The movements LIST endpoint returns <c>TransactionListItemDto</c>, which has
    /// no traceability fields -- <c>categorySource</c>/<c>categorizationRuleId</c>
    /// only exist on the single-transaction DETAIL endpoint (point 11), so any
    /// assertion about them has to go through here.
    /// </summary>
    private static Task<JsonElement> GetDetailAsync(TestUser user, Guid transactionId) =>
        user.GetJsonAsync($"/api/v1/transactions/{transactionId}");

    private static async Task<JsonElement> CorrectCategoryAsync(
        TestUser user, Guid transactionId, Guid categoryId, bool createRule = true, bool applyToExistingMatches = false)
    {
        var response = await user.Client.PutAsJsonAsync(
            $"/api/v1/transactions/{transactionId}/category",
            new { categoryId, createRule, applyToExistingMatches });
        await response.EnsureOkAsync();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// Point 24a: a movement is uncategorized, the user corrects it and keeps the
    /// default "aplicar también a movimientos similares" (createRule=true), and a
    /// later import of a similarly-worded but not identical movement is
    /// automatically categorized the same way -- traceable as coming from the
    /// user's own rule (point 11).
    /// </summary>
    [Fact]
    public async Task Correction_teaches_a_rule_that_categorizes_future_movements_with_traceability()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: null);

        var firstItems = await user.ConfirmAndListAsync(account, OneRowStatement("05/03/2026", "LAVANDERIA EXPRESS SUR", 6.50m));
        var first = FindByDescription(firstItems, "LAVANDERIA EXPRESS");
        Assert.Equal("Otros", first.GetProperty("categoryName").GetString());
        // No rule matched, so it fell back to the generic bucket -- "Imported", not
        // "Uncategorized" (that source means no category at all, CategoryId null).
        Assert.Equal("Imported", (await GetDetailAsync(user, first.GetProperty("id").GetGuid())).GetProperty("categorySource").GetString());

        var servicios = await FindCategoryAsync(user, "SERVICIOS");
        var detail = await CorrectCategoryAsync(user, first.GetProperty("id").GetGuid(), servicios.GetProperty("id").GetGuid());

        Assert.Equal("Manual", detail.GetProperty("categorySource").GetString());
        Assert.True(detail.GetProperty("categoryManuallySet").GetBoolean());

        var rules = await user.GetJsonAsync("/api/v1/categorization-rules");
        var rule = rules.EnumerateArray().First(r => r.GetProperty("pattern").GetString() == "LAVANDERIA");
        Assert.Equal("Servicios", rule.GetProperty("categoryName").GetString());
        Assert.Equal("Contains", rule.GetProperty("matchType").GetString());

        var secondItems = await user.ConfirmAndListAsync(account, OneRowStatement("20/03/2026", "LAVANDERIA EXPRESS NORTE 84931", 9.10m));
        var second = FindByDescription(secondItems, "LAVANDERIA EXPRESS NORTE");
        Assert.Equal("Servicios", second.GetProperty("categoryName").GetString());

        var secondDetail = await GetDetailAsync(user, second.GetProperty("id").GetGuid());
        Assert.Equal("UserRule", secondDetail.GetProperty("categorySource").GetString());
        Assert.Equal(rule.GetProperty("id").GetGuid(), secondDetail.GetProperty("categorizationRuleId").GetGuid());
    }

    /// <summary>
    /// Point 6/19: the preview endpoint answers "if I create this rule, which
    /// movements would match?" before anything is saved.
    /// </summary>
    [Fact]
    public async Task Preview_reports_the_pattern_and_match_count_without_changing_anything()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: null);

        var items = await user.ConfirmAndListAsync(account, TwoRowStatement(
            "05/03/2026", "TALLER MECANICO RAFAEL SUR", 25m,
            "06/03/2026", "TALLER MECANICO RAFAEL NORTE", 40m));
        var first = FindByDescription(items, "TALLER MECANICO RAFAEL SUR");
        var second = FindByDescription(items, "TALLER MECANICO RAFAEL NORTE");

        var transporte = await FindCategoryAsync(user, "TRANSPORTE");
        var preview = await (await user.Client.PostAsJsonAsync("/api/v1/categorization-rules/preview", new
        {
            transactionId = first.GetProperty("id").GetGuid(),
            categoryId = transporte.GetProperty("id").GetGuid(),
        })).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("TALLER", preview.GetProperty("pattern").GetString());
        Assert.False(preview.GetProperty("isTooGeneric").GetBoolean());
        Assert.Equal(1, preview.GetProperty("matchedCount").GetInt32());
        Assert.Contains(
            preview.GetProperty("sample").EnumerateArray(),
            s => s.GetProperty("id").GetGuid() == second.GetProperty("id").GetGuid());

        // Nothing was persisted -- no rule exists yet and the movements are untouched.
        var rules = await user.GetJsonAsync("/api/v1/categorization-rules");
        Assert.Empty(rules.EnumerateArray());
        var stillOtros = (await user.GetJsonAsync("/api/v1/transactions")).GetProperty("items");
        Assert.Equal("Otros", FindByDescription(stillOtros, "TALLER MECANICO RAFAEL NORTE").GetProperty("categoryName").GetString());
    }

    /// <summary>
    /// Point 7/16/24b: "este, anteriores y futuros" only recategorizes history once
    /// the user explicitly confirms (here, in the same request that sets the rule),
    /// reports how many movements changed, and future movements keep matching too.
    /// </summary>
    [Fact]
    public async Task Applying_to_existing_matches_recategorizes_history_once_and_still_covers_future_movements()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: null);

        var items = await user.ConfirmAndListAsync(account, TwoRowStatement(
            "05/03/2026", "TALLER MECANICO RAFAEL SUR", 25m,
            "06/03/2026", "TALLER MECANICO RAFAEL NORTE", 40m));
        var first = FindByDescription(items, "TALLER MECANICO RAFAEL SUR");
        var second = FindByDescription(items, "TALLER MECANICO RAFAEL NORTE");
        Assert.Equal("Otros", second.GetProperty("categoryName").GetString());

        var transporte = await FindCategoryAsync(user, "TRANSPORTE");
        var detail = await CorrectCategoryAsync(
            user, first.GetProperty("id").GetGuid(), transporte.GetProperty("id").GetGuid(),
            createRule: true, applyToExistingMatches: true);

        // Exactly the other pre-existing movement was recategorized -- never the
        // one just corrected by hand (that one is already Manual).
        Assert.Equal(1, detail.GetProperty("recategorizedCount").GetInt32());

        var afterItems = (await user.GetJsonAsync("/api/v1/transactions")).GetProperty("items");
        var recategorized = FindByDescription(afterItems, "TALLER MECANICO RAFAEL NORTE");
        Assert.Equal("Transporte", recategorized.GetProperty("categoryName").GetString());
        var recategorizedDetail = await GetDetailAsync(user, recategorized.GetProperty("id").GetGuid());
        Assert.Equal("UserRule", recategorizedDetail.GetProperty("categorySource").GetString());

        // Point 8/9: a brand new movement from the same shop, in a later import,
        // is categorized automatically too.
        var thirdItems = await user.ConfirmAndListAsync(account, OneRowStatement("10/04/2026", "TALLER MECANICO RAFAEL CENTRO", 15m));
        var third = FindByDescription(thirdItems, "TALLER MECANICO RAFAEL CENTRO");
        Assert.Equal("Transporte", third.GetProperty("categoryName").GetString());
        var thirdDetail = await GetDetailAsync(user, third.GetProperty("id").GetGuid());
        Assert.Equal("UserRule", thirdDetail.GetProperty("categorySource").GetString());
    }

    /// <summary>
    /// Point 24c: each user's correction to the same merchant produces their own,
    /// independent rule. NETFLIX already has a seeded system rule (Suscripciones),
    /// so this also proves a personal rule can override the system default for one
    /// user without ever touching the other user's categorization.
    /// </summary>
    [Fact]
    public async Task Two_users_correcting_the_same_merchant_get_independent_categories()
    {
        var userA = await factory.RegisterUserAsync();
        var accountA = await userA.CreateAccountAsync(openingBalance: null);
        var userB = await factory.RegisterUserAsync();
        var accountB = await userB.CreateAccountAsync(openingBalance: null);

        var aFirst = FindByDescription(
            await userA.ConfirmAndListAsync(accountA, OneRowStatement("05/03/2026", "NETFLIX COM", 12.99m)), "NETFLIX");
        var bFirst = FindByDescription(
            await userB.ConfirmAndListAsync(accountB, OneRowStatement("05/03/2026", "NETFLIX COM", 12.99m)), "NETFLIX");

        // Both start from the same seeded system rule.
        Assert.Equal("Suscripciones", aFirst.GetProperty("categoryName").GetString());
        Assert.Equal("Suscripciones", bFirst.GetProperty("categoryName").GetString());

        // A keeps Suscripciones (still teaches A's own rule); B moves to Entretenimiento.
        var suscripciones = await FindCategoryAsync(userA, "SUSCRIPCIONES");
        await CorrectCategoryAsync(userA, aFirst.GetProperty("id").GetGuid(), suscripciones.GetProperty("id").GetGuid());

        var entretenimiento = await FindCategoryAsync(userB, "ENTRETENIMIENTO");
        await CorrectCategoryAsync(userB, bFirst.GetProperty("id").GetGuid(), entretenimiento.GetProperty("id").GetGuid());

        var aSecond = FindByDescription(
            await userA.ConfirmAndListAsync(accountA, OneRowStatement("20/03/2026", "NETFLIX COM BILL", 12.99m)), "NETFLIX COM BILL");
        var bSecond = FindByDescription(
            await userB.ConfirmAndListAsync(accountB, OneRowStatement("20/03/2026", "NETFLIX COM BILL", 12.99m)), "NETFLIX COM BILL");

        Assert.Equal("Suscripciones", aSecond.GetProperty("categoryName").GetString());
        Assert.Equal("Entretenimiento", bSecond.GetProperty("categoryName").GetString());

        var aRules = await userA.GetJsonAsync("/api/v1/categorization-rules");
        var bRules = await userB.GetJsonAsync("/api/v1/categorization-rules");
        Assert.All(aRules.EnumerateArray(), r => Assert.Equal("Suscripciones", r.GetProperty("categoryName").GetString()));
        Assert.All(bRules.EnumerateArray(), r => Assert.Equal("Entretenimiento", r.GetProperty("categoryName").GetString()));
    }

    /// <summary>
    /// Point 12: when a correction's suggested pattern would collide with an
    /// existing rule for a different category, Nexo escalates to a more specific
    /// pattern instead of silently repainting the broader rule -- the same
    /// "UBER" vs "UBER EATS" shape the spec describes, reproduced through the
    /// ordinary "correct a movement" flow rather than the rules-management screen.
    /// </summary>
    [Fact]
    public async Task A_more_specific_rule_is_created_instead_of_corrupting_a_broader_one()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: null);

        var comida = await FindCategoryAsync(user, "COMIDA");
        var transporte = await FindCategoryAsync(user, "TRANSPORTE");

        var juiceBar = FindByDescription(
            await user.ConfirmAndListAsync(account, OneRowStatement("05/03/2026", "ZUMO NATURAL PARQUE", 3.50m)), "ZUMO NATURAL");
        await CorrectCategoryAsync(user, juiceBar.GetProperty("id").GetGuid(), comida.GetProperty("id").GetGuid());

        var bikeShop = FindByDescription(
            await user.ConfirmAndListAsync(account, OneRowStatement("06/03/2026", "ZUMO EXPRESS BAR", 4.20m)), "ZUMO EXPRESS");
        await CorrectCategoryAsync(user, bikeShop.GetProperty("id").GetGuid(), transporte.GetProperty("id").GetGuid());

        var rules = (await user.GetJsonAsync("/api/v1/categorization-rules")).EnumerateArray().ToList();
        var broad = rules.First(r => r.GetProperty("pattern").GetString() == "ZUMO");
        var narrow = rules.First(r => r.GetProperty("pattern").GetString() == "ZUMO EXPRESS");
        Assert.Equal("Comida", broad.GetProperty("categoryName").GetString());
        Assert.Equal("Transporte", narrow.GetProperty("categoryName").GetString());

        // The broader rule still serves anything that isn't the more specific brand.
        var thirdItems = await user.ConfirmAndListAsync(account, OneRowStatement("10/03/2026", "ZUMO NATURAL NORTE", 3.80m));
        Assert.Equal("Comida", FindByDescription(thirdItems, "ZUMO NATURAL NORTE").GetProperty("categoryName").GetString());

        // The more specific rule wins when both would otherwise match.
        var fourthItems = await user.ConfirmAndListAsync(account, OneRowStatement("11/03/2026", "ZUMO EXPRESS CENTRO", 4.50m));
        Assert.Equal("Transporte", FindByDescription(fourthItems, "ZUMO EXPRESS CENTRO").GetProperty("categoryName").GetString());
    }

    /// <summary>Point 13: a contradictory pattern is detected, never silently duplicated.</summary>
    [Fact]
    public async Task Creating_a_rule_for_a_pattern_that_already_belongs_to_another_category_is_a_conflict()
    {
        var user = await factory.RegisterUserAsync();
        var salud = await FindCategoryAsync(user, "SALUD");
        var entretenimiento = await FindCategoryAsync(user, "ENTRETENIMIENTO");

        var created = await user.Client.PostAsJsonAsync("/api/v1/categorization-rules", new
        {
            pattern = "GIMNASIO",
            matchType = "Contains",
            categoryId = salud.GetProperty("id").GetGuid(),
        });
        await created.EnsureOkAsync();

        var conflict = await user.Client.PostAsJsonAsync("/api/v1/categorization-rules", new
        {
            pattern = "GIMNASIO",
            matchType = "Contains",
            categoryId = entretenimiento.GetProperty("id").GetGuid(),
        });

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var body = await conflict.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Salud", body.GetProperty("existingCategoryName").GetString());
    }

    /// <summary>
    /// Point 18: the backend rejects a dangerously generic pattern regardless of
    /// what a mobile client would have allowed.
    /// </summary>
    [Theory]
    [InlineData("PAGO")]
    [InlineData("A")]
    [InlineData("TRANSFERENCIA")]
    public async Task A_pattern_with_too_little_information_is_rejected(string pattern)
    {
        var user = await factory.RegisterUserAsync();
        var otros = await FindCategoryAsync(user, "OTROS");

        var response = await user.Client.PostAsJsonAsync("/api/v1/categorization-rules", new
        {
            pattern,
            matchType = "Contains",
            categoryId = otros.GetProperty("id").GetGuid(),
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    /// <summary>Point 17: deleting a rule only stops future matches. History is never rewritten.</summary>
    [Fact]
    public async Task Deleting_a_rule_stops_future_matches_but_keeps_past_categorizations()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: null);

        var comida = await FindCategoryAsync(user, "COMIDA");
        var first = FindByDescription(
            await user.ConfirmAndListAsync(account, OneRowStatement("05/03/2026", "PANADERIA SAN JOSE CENTRO", 2.50m)), "PANADERIA SAN JOSE");
        await CorrectCategoryAsync(user, first.GetProperty("id").GetGuid(), comida.GetProperty("id").GetGuid());

        var rules = await user.GetJsonAsync("/api/v1/categorization-rules");
        var ruleId = rules.EnumerateArray().First(r => r.GetProperty("pattern").GetString() == "PANADERIA").GetProperty("id").GetGuid();

        await (await user.Client.DeleteAsync($"/api/v1/categorization-rules/{ruleId}")).EnsureOkAsync();

        var secondItems = await user.ConfirmAndListAsync(account, OneRowStatement("06/03/2026", "PANADERIA SAN JOSE NORTE", 3.10m));
        var second = FindByDescription(secondItems, "PANADERIA SAN JOSE NORTE");
        Assert.Equal("Otros", second.GetProperty("categoryName").GetString());

        var firstAfterDelete = FindByDescription(secondItems, "PANADERIA SAN JOSE CENTRO");
        Assert.Equal("Comida", firstAfterDelete.GetProperty("categoryName").GetString());
    }

    /// <summary>
    /// Point 14/21: UserId always comes from the auth token, never the client, and
    /// a rule id or transaction id that belongs to another user is indistinguishable
    /// from one that does not exist -- 404, never 403, on every operation.
    /// </summary>
    [Fact]
    public async Task A_user_cannot_read_update_delete_or_preview_against_another_users_rules_or_movements()
    {
        var owner = await factory.RegisterUserAsync();
        var ownerAccount = await owner.CreateAccountAsync(openingBalance: null);
        var salud = await FindCategoryAsync(owner, "SALUD");

        var ownerRuleResponse = await owner.Client.PostAsJsonAsync("/api/v1/categorization-rules", new
        {
            pattern = "KIOSKO",
            matchType = "Contains",
            categoryId = salud.GetProperty("id").GetGuid(),
        });
        await ownerRuleResponse.EnsureOkAsync();
        var ownerRuleId = (await ownerRuleResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var ownerTransaction = FindByDescription(
            await owner.ConfirmAndListAsync(ownerAccount, OneRowStatement("05/03/2026", "KIOSKO KM 5", 1.50m)), "KIOSKO");

        var intruder = await factory.RegisterUserAsync();

        var listResult = await intruder.GetJsonAsync("/api/v1/categorization-rules");
        Assert.DoesNotContain(listResult.EnumerateArray(), r => r.GetProperty("id").GetGuid() == ownerRuleId);

        var updateResponse = await intruder.Client.PutAsJsonAsync(
            $"/api/v1/categorization-rules/{ownerRuleId}",
            new { categoryId = salud.GetProperty("id").GetGuid(), isActive = false });
        Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);

        var deleteResponse = await intruder.Client.DeleteAsync($"/api/v1/categorization-rules/{ownerRuleId}");
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);

        var previewResponse = await intruder.Client.PostAsJsonAsync("/api/v1/categorization-rules/preview", new
        {
            transactionId = ownerTransaction.GetProperty("id").GetGuid(),
            categoryId = salud.GetProperty("id").GetGuid(),
        });
        Assert.Equal(HttpStatusCode.NotFound, previewResponse.StatusCode);

        // The intruder's own rule set stays completely unaffected by the owner's rule.
        var afterOwnerRule = FindByDescription(
            await intruder.ConfirmAndListAsync(
                await intruder.CreateAccountAsync(openingBalance: null),
                OneRowStatement("06/03/2026", "KIOSKO KM 5", 1.50m)),
            "KIOSKO");
        Assert.Equal("Otros", afterOwnerRule.GetProperty("categoryName").GetString());
    }

    /// <summary>
    /// Point 16: editing a rule directly from "Reglas de categorización"
    /// (PUT /categorization-rules/{id}, never through a transaction correction)
    /// recategorizes history only when explicitly asked, and -- since the rule
    /// itself is what changed -- every future import keeps matching it under the
    /// new category too. Every other integration test that exercises "actualizar
    /// regla" goes through the transaction-correction endpoint; this is the only
    /// one that calls the rules screen's own PUT directly.
    /// </summary>
    [Fact]
    public async Task Updating_a_rule_directly_retargets_history_and_future_movements()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: null);

        var servicios = await FindCategoryAsync(user, "SERVICIOS");
        var salud = await FindCategoryAsync(user, "SALUD");

        var first = FindByDescription(
            await user.ConfirmAndListAsync(account, OneRowStatement("05/03/2026", "SPA RELAX WELLNESS", 30m)), "SPA RELAX");
        await CorrectCategoryAsync(user, first.GetProperty("id").GetGuid(), servicios.GetProperty("id").GetGuid());

        var second = FindByDescription(
            await user.ConfirmAndListAsync(account, OneRowStatement("06/03/2026", "SPA RELAX CENTRO", 45m)), "SPA RELAX CENTRO");
        Assert.Equal("Servicios", second.GetProperty("categoryName").GetString());

        var rules = await user.GetJsonAsync("/api/v1/categorization-rules");
        var ruleId = rules.EnumerateArray().First(r => r.GetProperty("pattern").GetString() == "SPA").GetProperty("id").GetGuid();

        var updateResponse = await user.Client.PutAsJsonAsync($"/api/v1/categorization-rules/{ruleId}", new
        {
            categoryId = salud.GetProperty("id").GetGuid(),
            isActive = true,
            applyToExistingMatches = true,
        });
        await updateResponse.EnsureOkAsync();

        // Both pre-existing movements -- the one that originally taught the rule
        // and the one it later matched -- move to the rule's new category.
        var afterUpdate = (await user.GetJsonAsync("/api/v1/transactions")).GetProperty("items");
        Assert.Equal("Salud", FindByDescription(afterUpdate, "SPA RELAX WELLNESS").GetProperty("categoryName").GetString());
        Assert.Equal("Salud", FindByDescription(afterUpdate, "SPA RELAX CENTRO").GetProperty("categoryName").GetString());

        // A brand new movement in a later import follows the rule's updated category.
        var thirdItems = await user.ConfirmAndListAsync(account, OneRowStatement("10/04/2026", "SPA RELAX NORTE", 20m));
        var third = FindByDescription(thirdItems, "SPA RELAX NORTE");
        Assert.Equal("Salud", third.GetProperty("categoryName").GetString());
        var thirdDetail = await GetDetailAsync(user, third.GetProperty("id").GetGuid());
        Assert.Equal("UserRule", thirdDetail.GetProperty("categorySource").GetString());
        Assert.Equal(ruleId, thirdDetail.GetProperty("categorizationRuleId").GetGuid());
    }

    /// <summary>
    /// Point 12/16 ("solo este movimiento"): correcting ONE movement that a
    /// personal rule already categorized, without asking to also update the rule,
    /// never touches the rule or any other movement it already matched -- only
    /// this one transaction becomes Manual. This is the default/no-op path of
    /// "¿solo este movimiento o también actualizar la regla?"; the "actualizar
    /// regla" branch is <see cref="Updating_a_rule_directly_retargets_history_and_future_movements"/>.
    /// </summary>
    [Fact]
    public async Task Correcting_a_single_rule_matched_movement_leaves_the_rule_and_its_other_matches_untouched()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: null);

        var transporte = await FindCategoryAsync(user, "TRANSPORTE");
        var otros = await FindCategoryAsync(user, "OTROS");

        var first = FindByDescription(
            await user.ConfirmAndListAsync(account, OneRowStatement("05/03/2026", "PARQUEADERO CENTRO SUR", 2m)), "PARQUEADERO CENTRO");
        await CorrectCategoryAsync(user, first.GetProperty("id").GetGuid(), transporte.GetProperty("id").GetGuid());

        var second = FindByDescription(
            await user.ConfirmAndListAsync(account, OneRowStatement("06/03/2026", "PARQUEADERO CENTRO NORTE", 3m)), "PARQUEADERO CENTRO NORTE");
        Assert.Equal("Transporte", second.GetProperty("categoryName").GetString());
        var secondId = second.GetProperty("id").GetGuid();

        var rulesBefore = await user.GetJsonAsync("/api/v1/categorization-rules");
        var ruleBefore = rulesBefore.EnumerateArray().First(r => r.GetProperty("pattern").GetString() == "PARQUEADERO CENTRO");

        // "Solo este movimiento": createRule=false, applyToExistingMatches=false --
        // the same request the mobile "Solo este movimiento" button sends.
        await CorrectCategoryAsync(user, secondId, otros.GetProperty("id").GetGuid(), createRule: false, applyToExistingMatches: false);

        var secondDetail = await GetDetailAsync(user, secondId);
        Assert.Equal("Otros", secondDetail.GetProperty("categoryName").GetString());
        Assert.Equal("Manual", secondDetail.GetProperty("categorySource").GetString());
        Assert.True(secondDetail.GetProperty("categoryManuallySet").GetBoolean());

        // The first movement -- the one the rule was originally learned from --
        // is completely unaffected by correcting the second one.
        var firstDetail = await GetDetailAsync(user, first.GetProperty("id").GetGuid());
        Assert.Equal("Transporte", firstDetail.GetProperty("categoryName").GetString());

        // The rule itself is untouched: same category, same id.
        var rulesAfter = await user.GetJsonAsync("/api/v1/categorization-rules");
        var ruleAfter = rulesAfter.EnumerateArray().First(r => r.GetProperty("id").GetGuid() == ruleBefore.GetProperty("id").GetGuid());
        Assert.Equal(ruleBefore.GetProperty("categoryId").GetGuid(), ruleAfter.GetProperty("categoryId").GetGuid());
        Assert.Equal("Transporte", ruleAfter.GetProperty("categoryName").GetString());

        // A third, later movement the rule matches still gets the rule's original
        // category -- the one-off manual correction above never leaked into it.
        var thirdItems = await user.ConfirmAndListAsync(account, OneRowStatement("10/04/2026", "PARQUEADERO CENTRO ESTE", 4m));
        var third = FindByDescription(thirdItems, "PARQUEADERO CENTRO ESTE");
        Assert.Equal("Transporte", third.GetProperty("categoryName").GetString());
    }
}
