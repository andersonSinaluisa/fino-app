using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Entregable 13 ("Transferencias internas"): a movement of money between the
/// person's own accounts should never be counted as real income or spending.
/// GET /api/v1/transfers/candidates finds the pairs (same amount, opposite
/// direction, different accounts, a few days apart); confirming one flags both
/// legs so the home summary, the category breakdown and every insight stop
/// treating the money as having entered or left the household.
/// </summary>
public class InternalTransferTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private static string OneRow(string date, string concept, string reference, decimal? debit, decimal? credit) =>
        $"""
        Banco Pichincha
        Fecha,Concepto,Documento,Debito,Credito
        {date},{concept},{reference},{debit?.ToString("0.00") ?? ""},{credit?.ToString("0.00") ?? ""}
        """;

    [Fact]
    public async Task Detects_a_same_amount_opposite_direction_candidate_across_accounts_within_the_window()
    {
        var user = await factory.RegisterUserAsync();
        var checking = await user.CreateAccountAsync(openingBalance: null);
        var savings = await user.CreateAccountAsync(openingBalance: null);

        await user.ConfirmImportAsync((await user.UploadStatementAsync(
            checking, OneRow("05/03/2026", "TRANSFERENCIA A AHORROS", "TRX-9001", 300.00m, null)))
            .GetProperty("importId").GetGuid());
        await user.ConfirmImportAsync((await user.UploadStatementAsync(
            savings, OneRow("06/03/2026", "TRANSFERENCIA DESDE CORRIENTE", "TRX-9002", null, 300.00m)))
            .GetProperty("importId").GetGuid());

        var candidates = await user.GetJsonAsync("/api/v1/transfers/candidates");

        Assert.Equal(1, candidates.GetArrayLength());
        var candidate = candidates[0];
        Assert.Equal(checking, candidate.GetProperty("outgoingAccountId").GetGuid());
        Assert.Equal(savings, candidate.GetProperty("incomingAccountId").GetGuid());
        Assert.Equal(300.00m, candidate.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Does_not_suggest_a_pair_whose_amounts_do_not_match()
    {
        var user = await factory.RegisterUserAsync();
        var checking = await user.CreateAccountAsync(openingBalance: null);
        var savings = await user.CreateAccountAsync(openingBalance: null);

        await user.ConfirmImportAsync((await user.UploadStatementAsync(
            checking, OneRow("05/03/2026", "TRANSFERENCIA A AHORROS", "TRX-9101", 300.00m, null)))
            .GetProperty("importId").GetGuid());
        await user.ConfirmImportAsync((await user.UploadStatementAsync(
            savings, OneRow("06/03/2026", "DEPOSITO", "TRX-9102", null, 250.00m)))
            .GetProperty("importId").GetGuid());

        var candidates = await user.GetJsonAsync("/api/v1/transfers/candidates");

        Assert.Equal(0, candidates.GetArrayLength());
    }

    [Fact]
    public async Task Does_not_suggest_a_pair_further_apart_than_the_window()
    {
        var user = await factory.RegisterUserAsync();
        var checking = await user.CreateAccountAsync(openingBalance: null);
        var savings = await user.CreateAccountAsync(openingBalance: null);

        await user.ConfirmImportAsync((await user.UploadStatementAsync(
            checking, OneRow("01/03/2026", "TRANSFERENCIA A AHORROS", "TRX-9201", 300.00m, null)))
            .GetProperty("importId").GetGuid());
        await user.ConfirmImportAsync((await user.UploadStatementAsync(
            savings, OneRow("07/03/2026", "DEPOSITO", "TRX-9202", null, 300.00m)))
            .GetProperty("importId").GetGuid());

        var candidates = await user.GetJsonAsync("/api/v1/transfers/candidates");

        Assert.Equal(0, candidates.GetArrayLength());
    }

    [Fact]
    public async Task Confirming_a_candidate_links_both_legs_and_removes_it_from_future_candidates()
    {
        var user = await factory.RegisterUserAsync();
        var checking = await user.CreateAccountAsync(openingBalance: null);
        var savings = await user.CreateAccountAsync(openingBalance: null);

        await user.ConfirmImportAsync((await user.UploadStatementAsync(
            checking, OneRow("05/03/2026", "TRANSFERENCIA A AHORROS", "TRX-9301", 300.00m, null)))
            .GetProperty("importId").GetGuid());
        await user.ConfirmImportAsync((await user.UploadStatementAsync(
            savings, OneRow("06/03/2026", "TRANSFERENCIA DESDE CORRIENTE", "TRX-9302", null, 300.00m)))
            .GetProperty("importId").GetGuid());

        var candidate = (await user.GetJsonAsync("/api/v1/transfers/candidates"))[0];
        var outgoingId = candidate.GetProperty("outgoingTransactionId").GetGuid();
        var incomingId = candidate.GetProperty("incomingTransactionId").GetGuid();

        var confirm = await user.Client.PostAsJsonAsync(
            "/api/v1/transfers/confirm",
            new { outgoingTransactionId = outgoingId, incomingTransactionId = incomingId });
        await confirm.EnsureOkAsync();
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

        var remaining = await user.GetJsonAsync("/api/v1/transfers/candidates");
        Assert.Equal(0, remaining.GetArrayLength());

        var outgoing = await user.GetJsonAsync($"/api/v1/transactions/{outgoingId}");
        Assert.True(outgoing.GetProperty("isInternalTransfer").GetBoolean());
        Assert.Equal(incomingId, outgoing.GetProperty("internalTransferLinkId").GetGuid());

        var incoming = await user.GetJsonAsync($"/api/v1/transactions/{incomingId}");
        Assert.True(incoming.GetProperty("isInternalTransfer").GetBoolean());
        Assert.Equal(outgoingId, incoming.GetProperty("internalTransferLinkId").GetGuid());
    }

    [Fact]
    public async Task A_confirmed_transfer_no_longer_counts_as_income_or_expense_in_the_monthly_summary()
    {
        var user = await factory.RegisterUserAsync();
        var checking = await user.CreateAccountAsync(openingBalance: null);
        var savings = await user.CreateAccountAsync(openingBalance: null);

        // Each account also has one genuine, non-transfer movement so the test can
        // tell "excluded" apart from "everything happens to be zero".
        var checkingCsv = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            05/03/2026,TRANSFERENCIA A AHORROS,TRX-9401,300.00,
            04/03/2026,MI COMISARIATO,TRX-9402,50.00,
            """;
        var savingsCsv = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            06/03/2026,TRANSFERENCIA DESDE CORRIENTE,TRX-9403,,300.00
            07/03/2026,ACREDITACION NOMINA,TRX-9404,,1000.00
            """;

        await user.ConfirmImportAsync((await user.UploadStatementAsync(checking, checkingCsv)).GetProperty("importId").GetGuid());
        await user.ConfirmImportAsync((await user.UploadStatementAsync(savings, savingsCsv)).GetProperty("importId").GetGuid());

        var candidate = (await user.GetJsonAsync("/api/v1/transfers/candidates"))[0];
        var confirmResponse = await user.Client.PostAsJsonAsync("/api/v1/transfers/confirm", new
        {
            outgoingTransactionId = candidate.GetProperty("outgoingTransactionId").GetGuid(),
            incomingTransactionId = candidate.GetProperty("incomingTransactionId").GetGuid(),
        });
        await confirmResponse.EnsureOkAsync();

        var summary = await user.GetJsonAsync("/api/v1/summary");

        Assert.Equal(50.00m, summary.GetProperty("month").GetProperty("expense").GetDecimal());
        Assert.Equal(1000.00m, summary.GetProperty("month").GetProperty("income").GetDecimal());
    }

    [Fact]
    public async Task Clearing_a_confirmed_transfer_undoes_both_legs()
    {
        var user = await factory.RegisterUserAsync();
        var checking = await user.CreateAccountAsync(openingBalance: null);
        var savings = await user.CreateAccountAsync(openingBalance: null);

        await user.ConfirmImportAsync((await user.UploadStatementAsync(
            checking, OneRow("05/03/2026", "TRANSFERENCIA A AHORROS", "TRX-9501", 300.00m, null)))
            .GetProperty("importId").GetGuid());
        await user.ConfirmImportAsync((await user.UploadStatementAsync(
            savings, OneRow("06/03/2026", "TRANSFERENCIA DESDE CORRIENTE", "TRX-9502", null, 300.00m)))
            .GetProperty("importId").GetGuid());

        var candidate = (await user.GetJsonAsync("/api/v1/transfers/candidates"))[0];
        var outgoingId = candidate.GetProperty("outgoingTransactionId").GetGuid();
        var incomingId = candidate.GetProperty("incomingTransactionId").GetGuid();

        await (await user.Client.PostAsJsonAsync(
            "/api/v1/transfers/confirm",
            new { outgoingTransactionId = outgoingId, incomingTransactionId = incomingId })).EnsureOkAsync();

        var clearResponse = await user.Client.PostAsync($"/api/v1/transfers/{outgoingId}/clear", content: null);
        await clearResponse.EnsureOkAsync();

        var outgoing = await user.GetJsonAsync($"/api/v1/transactions/{outgoingId}");
        Assert.False(outgoing.GetProperty("isInternalTransfer").GetBoolean());
        Assert.Equal(JsonValueKind.Null, outgoing.GetProperty("internalTransferLinkId").ValueKind);

        var incoming = await user.GetJsonAsync($"/api/v1/transactions/{incomingId}");
        Assert.False(incoming.GetProperty("isInternalTransfer").GetBoolean());

        // Undone -- back to being a real movement, so it is a candidate again.
        var candidatesAgain = await user.GetJsonAsync("/api/v1/transfers/candidates");
        Assert.Equal(1, candidatesAgain.GetArrayLength());
    }

    [Fact]
    public async Task A_user_cannot_confirm_a_transfer_using_another_users_transactions()
    {
        var owner = await factory.RegisterUserAsync();
        var checking = await owner.CreateAccountAsync(openingBalance: null);
        var savings = await owner.CreateAccountAsync(openingBalance: null);

        await owner.ConfirmImportAsync((await owner.UploadStatementAsync(
            checking, OneRow("05/03/2026", "TRANSFERENCIA A AHORROS", "TRX-9601", 300.00m, null)))
            .GetProperty("importId").GetGuid());
        await owner.ConfirmImportAsync((await owner.UploadStatementAsync(
            savings, OneRow("06/03/2026", "TRANSFERENCIA DESDE CORRIENTE", "TRX-9602", null, 300.00m)))
            .GetProperty("importId").GetGuid());

        var candidate = (await owner.GetJsonAsync("/api/v1/transfers/candidates"))[0];

        var intruder = await factory.RegisterUserAsync();
        var response = await intruder.Client.PostAsJsonAsync("/api/v1/transfers/confirm", new
        {
            outgoingTransactionId = candidate.GetProperty("outgoingTransactionId").GetGuid(),
            incomingTransactionId = candidate.GetProperty("incomingTransactionId").GetGuid(),
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Confirming_two_movements_on_the_same_account_is_rejected()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var csv = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            05/03/2026,RETIRO,TRX-9701,100.00,
            05/03/2026,DEPOSITO,TRX-9702,,100.00
            """;
        await user.ConfirmImportAsync((await user.UploadStatementAsync(accountId, csv)).GetProperty("importId").GetGuid());

        var list = await user.GetJsonAsync("/api/v1/transactions");
        var expenseId = list.GetProperty("items").EnumerateArray()
            .First(t => t.GetProperty("direction").GetString() == "Expense")
            .GetProperty("id").GetGuid();
        var incomeId = list.GetProperty("items").EnumerateArray()
            .First(t => t.GetProperty("direction").GetString() == "Income")
            .GetProperty("id").GetGuid();

        var response = await user.Client.PostAsJsonAsync("/api/v1/transfers/confirm", new
        {
            outgoingTransactionId = expenseId,
            incomingTransactionId = incomeId,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
