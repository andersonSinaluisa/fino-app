using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// §25: la conciliación de retiros, extremo a extremo contra la API real.
///
/// La prueba que da sentido a toda la funcionalidad es
/// <see cref="A_confirmed_withdrawal_moves_money_without_counting_as_spending"/>:
/// reproduce los números exactos del §7. Si esa pasa, la regla del §27 ("mover
/// dinero entre cuentas propias no es gastar dinero") se cumple de verdad en Home,
/// en estadísticas y en los saldos, y no solo en el papel.
/// </summary>
public class CashWithdrawalTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>El reloj de los tests está fijo en 10/03/2026, así que las fechas caen en "este mes".</summary>
    private static string Statement(params string[] rows) => $"""
        Banco Pichincha
        Fecha,Concepto,Documento,Debito,Credito
        {string.Join("\n", rows)}
        """;

    [Fact]
    public async Task A_withdrawal_shows_up_as_a_candidate_and_ordinary_spending_does_not()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync();

        await user.ConfirmAndListAsync(account, Statement(
            "05/03/2026,RETIRO ATM,TRX-001,100.00,",
            "06/03/2026,SUPERMAXI ALBORADA,TRX-002,48.20,"));

        var candidates = await user.GetJsonAsync("/api/v1/transfers/withdrawals/candidates");
        var items = candidates.EnumerateArray().ToArray();

        Assert.Single(items);
        Assert.Equal("RETIRO ATM", items[0].GetProperty("description").GetString());
        Assert.Equal(100.00m, items[0].GetProperty("amount").GetDecimal());
        Assert.Equal("High", items[0].GetProperty("confidence").GetString());
    }

    [Fact]
    public async Task An_atm_fee_is_never_offered_as_a_withdrawal()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync();

        // §15: el par que llega junto en todos los extractos reales. El de $100 es
        // una transferencia a efectivo; el de $0.50 es un gasto de verdad.
        await user.ConfirmAndListAsync(account, Statement(
            "05/03/2026,RETIRO ATM,TRX-001,100.00,",
            "05/03/2026,COMISION RETIRO ATM,TRX-002,0.50,"));

        var candidates = await user.GetJsonAsync("/api/v1/transfers/withdrawals/candidates");
        var descriptions = candidates.EnumerateArray()
            .Select(c => c.GetProperty("description").GetString())
            .ToArray();

        Assert.Contains("RETIRO ATM", descriptions);
        Assert.DoesNotContain("COMISION RETIRO ATM", descriptions);
    }

    [Fact]
    public async Task A_confirmed_withdrawal_moves_money_without_counting_as_spending()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: 500m);

        // §7, con sus cifras exactas: banco $500, efectivo $20, retiro de $100.
        await user.Client.PostAsJsonAsync("/api/v1/quick-entry/cash-balance", new { balance = 20m, mode = "Anchor" });
        await user.ConfirmAndListAsync(account, Statement("05/03/2026,RETIRO ATM,TRX-001,100.00,"));

        // Antes de conciliar, Fino lo cuenta como gasto -- que es precisamente el
        // problema que esta funcionalidad existe para arreglar.
        var before = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(100.00m, before.GetProperty("month").GetProperty("expense").GetDecimal());

        var candidate = (await user.GetJsonAsync("/api/v1/transfers/withdrawals/candidates"))[0];
        var withdrawalId = candidate.GetProperty("transactionId").GetGuid();

        var response = await user.Client.PostAsJsonAsync(
            $"/api/v1/transfers/withdrawals/{withdrawalId}/confirm",
            new { });
        await response.EnsureOkAsync();

        var confirmation = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.False(confirmation.GetProperty("matchedExistingCashMovement").GetBoolean());
        Assert.Equal(100.00m, confirmation.GetProperty("amount").GetDecimal());

        var accounts = await user.GetJsonAsync("/api/v1/accounts");
        var bank = accounts.EnumerateArray().First(a => a.GetProperty("providerCode").GetString() == "PICHINCHA");
        var cash = accounts.EnumerateArray().First(a => a.GetProperty("providerCode").GetString() == "EFECTIVO");

        // §7: banco $400, efectivo $120, patrimonio $520, gasto $0.
        Assert.Equal(400.00m, bank.GetProperty("balance").GetDecimal());
        Assert.Equal(120.00m, cash.GetProperty("balance").GetDecimal());

        var after = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(0m, after.GetProperty("month").GetProperty("expense").GetDecimal());
        Assert.Equal(520.00m, after.GetProperty("totalBalance").GetDecimal());

        // Y el retiro deja de ofrecerse.
        var remaining = await user.GetJsonAsync("/api/v1/transfers/withdrawals/candidates");
        Assert.Empty(remaining.EnumerateArray());
    }

    [Fact]
    public async Task Spending_the_withdrawn_cash_afterwards_is_real_spending()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: 500m);
        await user.ConfirmAndListAsync(account, Statement("05/03/2026,RETIRO ATM,TRX-001,100.00,"));

        var candidate = (await user.GetJsonAsync("/api/v1/transfers/withdrawals/candidates"))[0];
        await user.Client.PostAsJsonAsync(
            $"/api/v1/transfers/withdrawals/{candidate.GetProperty("transactionId").GetGuid()}/confirm",
            new { });

        // §26: "el usuario después registra 5 almuerzo... ese $5 sí aparece como gasto".
        await user.Client.PostAsJsonAsync("/api/v1/quick-entry/transactions", new { amount = 20m, description = "Almuerzo" });

        var summary = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(20.00m, summary.GetProperty("month").GetProperty("expense").GetDecimal());

        var accounts = await user.GetJsonAsync("/api/v1/accounts");
        var cash = accounts.EnumerateArray().First(a => a.GetProperty("providerCode").GetString() == "EFECTIVO");
        Assert.Equal(80.00m, cash.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task A_withdrawal_matches_cash_the_person_already_registered_by_hand()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: 500m);

        // §12: primero apunta el efectivo a mano, después llega el extracto.
        var manual = await user.Client.PostAsJsonAsync("/api/v1/quick-entry/transactions", new
        {
            amount = 100m,
            direction = "Income",
            description = "Saque del cajero",
            occurredAt = "2026-03-05T18:00:00Z",
        });
        await manual.EnsureOkAsync();
        var manualId = (await manual.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        await user.ConfirmAndListAsync(account, Statement("05/03/2026,RETIRO ATM,TRX-001,100.00,"));

        var candidate = (await user.GetJsonAsync("/api/v1/transfers/withdrawals/candidates"))[0];

        // Fino propone justamente ese ingreso.
        Assert.Equal(manualId, candidate.GetProperty("suggestedCashTransactionId").GetGuid());
        Assert.Equal(1, candidate.GetProperty("ambiguousMatchCount").GetInt32());

        var response = await user.Client.PostAsJsonAsync(
            $"/api/v1/transfers/withdrawals/{candidate.GetProperty("transactionId").GetGuid()}/confirm",
            new { cashTransactionId = manualId });
        await response.EnsureOkAsync();

        var confirmation = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(confirmation.GetProperty("matchedExistingCashMovement").GetBoolean());
        Assert.Equal(manualId, confirmation.GetProperty("cashTransactionId").GetGuid());

        // Lo esencial: NO se duplicó el efectivo. Sigue habiendo $100, no $200.
        var accounts = await user.GetJsonAsync("/api/v1/accounts");
        var cash = accounts.EnumerateArray().First(a => a.GetProperty("providerCode").GetString() == "EFECTIVO");
        Assert.Equal(100.00m, cash.GetProperty("balance").GetDecimal());

        var summary = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(0m, summary.GetProperty("month").GetProperty("expense").GetDecimal());
        Assert.Equal(0m, summary.GetProperty("month").GetProperty("income").GetDecimal());
    }

    [Fact]
    public async Task Two_possible_cash_matches_are_never_resolved_automatically()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: 500m);

        // §13: dos ingresos de $100 el mismo día. Elegir uno al azar podría dejar
        // descuadrados los dos movimientos reales.
        foreach (var _ in Enumerable.Range(0, 2))
        {
            await user.Client.PostAsJsonAsync("/api/v1/quick-entry/transactions", new
            {
                amount = 100m,
                direction = "Income",
                description = "Efectivo del cajero",
                occurredAt = "2026-03-05T18:00:00Z",
            });
        }

        await user.ConfirmAndListAsync(account, Statement("05/03/2026,RETIRO ATM,TRX-001,100.00,"));

        var candidate = (await user.GetJsonAsync("/api/v1/transfers/withdrawals/candidates"))[0];

        Assert.Equal(JsonValueKind.Null, candidate.GetProperty("suggestedCashTransactionId").ValueKind);
        Assert.Equal(2, candidate.GetProperty("ambiguousMatchCount").GetInt32());
    }

    [Fact]
    public async Task Rejecting_a_withdrawal_keeps_its_category_and_stops_the_question()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync();

        await user.ConfirmAndListAsync(account, Statement("05/03/2026,RETIRO ATM,TRX-001,100.00,"));

        var candidate = (await user.GetJsonAsync("/api/v1/transfers/withdrawals/candidates"))[0];
        var id = candidate.GetProperty("transactionId").GetGuid();

        var before = await user.GetJsonAsync($"/api/v1/transactions/{id}");
        var categoryBefore = before.GetProperty("categoryId").ToString();

        var response = await user.Client.PostAsync($"/api/v1/transfers/withdrawals/{id}/reject", content: null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // §10: sigue siendo un gasto, con su categoría intacta...
        var after = await user.GetJsonAsync($"/api/v1/transactions/{id}");
        Assert.False(after.GetProperty("isInternalTransfer").GetBoolean());
        Assert.Equal(categoryBefore, after.GetProperty("categoryId").ToString());

        // ...y Fino no vuelve a preguntarlo.
        var remaining = await user.GetJsonAsync("/api/v1/transfers/withdrawals/candidates");
        Assert.Empty(remaining.EnumerateArray());
    }

    [Fact]
    public async Task Confirming_creates_the_cash_account_when_there_is_none()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync();
        await user.ConfirmAndListAsync(account, Statement("05/03/2026,RETIRO ATM,TRX-001,100.00,"));

        // §11: todavía no existe cuenta Efectivo, y la persona no debería tener que
        // salir a crearla y volver a empezar.
        var before = await user.GetJsonAsync("/api/v1/accounts");
        Assert.DoesNotContain(before.EnumerateArray(), a => a.GetProperty("providerCode").GetString() == "EFECTIVO");

        var candidate = (await user.GetJsonAsync("/api/v1/transfers/withdrawals/candidates"))[0];
        var response = await user.Client.PostAsJsonAsync(
            $"/api/v1/transfers/withdrawals/{candidate.GetProperty("transactionId").GetGuid()}/confirm",
            new { });
        await response.EnsureOkAsync();

        var confirmation = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(confirmation.GetProperty("cashAccountCreated").GetBoolean());

        var after = await user.GetJsonAsync("/api/v1/accounts");
        Assert.Contains(after.EnumerateArray(), a => a.GetProperty("providerCode").GetString() == "EFECTIVO");
    }

    [Fact]
    public async Task Remembering_a_pattern_records_it_as_a_rule_for_that_bank()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync();
        await user.ConfirmAndListAsync(account, Statement("05/03/2026,RETIRO ATM,TRX-001,100.00,"));

        var candidate = (await user.GetJsonAsync("/api/v1/transfers/withdrawals/candidates"))[0];
        Assert.True(candidate.GetProperty("canLearnPattern").GetBoolean());

        var response = await user.Client.PostAsJsonAsync(
            $"/api/v1/transfers/withdrawals/{candidate.GetProperty("transactionId").GetGuid()}/confirm",
            new { rememberPattern = true });
        await response.EnsureOkAsync();

        var confirmation = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(confirmation.GetProperty("patternRemembered").GetBoolean());

        // §14: la regla aprendida es una CategorizationRule normal, atada al banco.
        // No hay entidad nueva que mantener en paralelo.
        var rules = await user.GetJsonAsync("/api/v1/categorization-rules");
        Assert.Contains(rules.EnumerateArray(), r => r.GetProperty("providerCode").GetString() == "PICHINCHA");
    }

    [Fact]
    public async Task A_bare_retiro_is_too_generic_to_become_a_rule()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync();
        await user.ConfirmAndListAsync(account, Statement("05/03/2026,RETIRO,TRX-001,100.00,"));

        var candidate = (await user.GetJsonAsync("/api/v1/transfers/withdrawals/candidates"))[0];

        // Se detecta como retiro...
        Assert.Equal(100.00m, candidate.GetProperty("amount").GetDecimal());
        // ...pero "RETIRO" a secas se aplicaría a cualquier movimiento de cualquier
        // banco, así que Fino no ofrece aprenderlo.
        Assert.False(candidate.GetProperty("canLearnPattern").GetBoolean());
    }

    [Fact]
    public async Task The_import_scan_counts_withdrawals_without_stopping_the_import()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync();

        var preview = await user.UploadStatementAsync(account, Statement(
            "05/03/2026,RETIRO ATM,TRX-001,100.00,",
            "06/03/2026,RETIRO CAJERO,TRX-002,40.00,",
            "07/03/2026,COMISION RETIRO ATM,TRX-003,0.50,",
            "08/03/2026,SUPERMAXI ALBORADA,TRX-004,48.20,"));

        var importId = preview.GetProperty("importId").GetGuid();
        await user.ConfirmImportAsync(importId);

        var scan = await user.GetJsonAsync($"/api/v1/transfers/withdrawals/scan/{importId}");

        // §8: "Encontramos 2 posibles retiros" -- la comisión y el consumo no cuentan.
        Assert.Equal(2, scan.GetProperty("candidateCount").GetInt32());
        Assert.Equal(2, scan.GetProperty("highConfidenceCount").GetInt32());
    }

    [Fact]
    public async Task One_persons_withdrawals_are_invisible_to_another()
    {
        var mine = await factory.RegisterUserAsync();
        var account = await mine.CreateAccountAsync();
        await mine.ConfirmAndListAsync(account, Statement("05/03/2026,RETIRO ATM,TRX-001,100.00,"));

        var candidate = (await mine.GetJsonAsync("/api/v1/transfers/withdrawals/candidates"))[0];
        var id = candidate.GetProperty("transactionId").GetGuid();

        var theirs = await factory.RegisterUserAsync();

        Assert.Empty((await theirs.GetJsonAsync("/api/v1/transfers/withdrawals/candidates")).EnumerateArray());

        var response = await theirs.Client.PostAsJsonAsync(
            $"/api/v1/transfers/withdrawals/{id}/confirm",
            new { });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
