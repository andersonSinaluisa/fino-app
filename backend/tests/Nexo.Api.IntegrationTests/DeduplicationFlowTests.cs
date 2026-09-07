using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Regression coverage for the bug found during the 2026-09-05 audit:
/// <c>DeduplicationService.CheckBatchAsync</c> filtered candidates with
/// <c>accountIds.Contains(t.FinancialAccountId)</c>, which — composed with Nexo's
/// global per-user query filter — does not translate on the SQLite provider this
/// suite runs against. Every import failed with a 500 before ever reaching the
/// matcher, regardless of bank, file format, or whether the account already had
/// movements. See docs/architecture.md ADR-009 and
/// <c>Nexo.Application.Common.QueryableGuidExtensions.WhereIdIn</c> for the fix.
///
/// This class walks the exact four-import sequence used to validate the fix, each
/// import building on the account state the previous one left behind — the shape
/// most likely to exercise a real user's history, not just an empty account.
/// </summary>
public class DeduplicationFlowTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private const string Import1ThreeNewMovements = """
        Banco Pichincha
        Fecha,Concepto,Documento,Debito,Credito
        15/03/2026,ACREDITACION ROL DE PAGOS,ROL-3001,,900.00
        16/03/2026,FARMACIA CRUZ AZUL,TRX-3001,15.50,
        17/03/2026,LA FAVORITA SUPERMERCADO,TRX-3002,42.30,
        """;

    // Byte-for-byte the same file as above.
    private const string Import2SameFile = Import1ThreeNewMovements;

    private const string Import3TwoExistingTwoNew = """
        Banco Pichincha
        Fecha,Concepto,Documento,Debito,Credito
        16/03/2026,FARMACIA CRUZ AZUL,TRX-3001,15.50,
        17/03/2026,LA FAVORITA SUPERMERCADO,TRX-3002,42.30,
        18/03/2026,NETFLIX.COM,TRX-3003,12.99,
        19/03/2026,UBER TRIP,TRX-3004,6.80,
        """;

    // Two real, distinct purchases: same merchant, same amount, three days apart.
    // Nothing like them exists in the database yet, so both must be recognised as
    // new -- the matcher only ever compares an incoming row against what is already
    // stored, never against another row in the same file (see
    // DeduplicationService's own comment on that design choice).
    private const string Import4TwoSimilarButDistinctMovements = """
        Banco Pichincha
        Fecha,Concepto,Documento,Debito,Credito
        20/03/2026,CAFETERIA JUAN VALDEZ,TRX-3005,4.50,
        23/03/2026,CAFETERIA JUAN VALDEZ,TRX-3006,4.50,
        """;

    [Fact]
    public async Task The_four_import_scenarios_from_the_audit_all_behave_correctly()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        // IMPORTACIÓN #1 -- 3 movimientos nuevos -> deben quedar 3.
        // This is also the exact shape that used to 500 before ever producing a
        // preview: CheckBatchAsync ran with zero stored candidates and still failed
        // to translate, because the failure is in the query's shape, not its data.
        var import1 = await user.UploadStatementAsync(accountId, Import1ThreeNewMovements);
        Assert.Equal("PreviewReady", import1.GetProperty("status").GetString());
        Assert.Equal(3, import1.GetProperty("newRows").GetInt32());
        Assert.Equal(0, import1.GetProperty("duplicateRows").GetInt32());
        await user.ConfirmImportAsync(import1.GetProperty("importId").GetGuid());

        var afterImport1 = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(3, afterImport1.GetProperty("totalCount").GetInt32());

        // IMPORTACIÓN #2 -- mismo archivo -> deben quedar los mismos 3.
        var import2 = await user.UploadStatementAsync(accountId, Import2SameFile);
        Assert.Equal(0, import2.GetProperty("newRows").GetInt32());
        Assert.Equal(3, import2.GetProperty("duplicateRows").GetInt32());
        await user.ConfirmImportAsync(import2.GetProperty("importId").GetGuid());

        var afterImport2 = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(3, afterImport2.GetProperty("totalCount").GetInt32());

        // IMPORTACIÓN #3 -- 2 existentes + 2 nuevos -> solamente agregar 2.
        var import3 = await user.UploadStatementAsync(accountId, Import3TwoExistingTwoNew);
        Assert.Equal(2, import3.GetProperty("newRows").GetInt32());
        Assert.Equal(2, import3.GetProperty("duplicateRows").GetInt32());
        await user.ConfirmImportAsync(import3.GetProperty("importId").GetGuid());

        var afterImport3 = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(5, afterImport3.GetProperty("totalCount").GetInt32());

        // IMPORTACIÓN #4 -- mismo monto, descripción similar, fechas distintas ->
        // ambos son movimientos legítimos y no deben colapsarse.
        var import4 = await user.UploadStatementAsync(accountId, Import4TwoSimilarButDistinctMovements);
        Assert.Equal(2, import4.GetProperty("newRows").GetInt32());
        Assert.Equal(0, import4.GetProperty("duplicateRows").GetInt32());
        await user.ConfirmImportAsync(import4.GetProperty("importId").GetGuid());

        // Persistencia + consulta de movimientos: la lista completa, con el alias de
        // cuenta resuelto para cada fila -- el otro punto que la misma consulta
        // intraducible rompía (TransactionService.LoadAccountAliasesAsync) en cuanto
        // hubiera al menos un movimiento que listar.
        var finalTransactions = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(7, finalTransactions.GetProperty("totalCount").GetInt32());
        foreach (var item in finalTransactions.GetProperty("items").EnumerateArray())
        {
            Assert.Equal("Banco Pichincha", item.GetProperty("accountAlias").GetString());
        }

        // Dashboard: la agregación debe reflejar exactamente lo confirmado.
        var summary = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(1, summary.GetProperty("accountCount").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(summary.GetProperty("greeting").GetString()));
    }

    /// <summary>
    /// Seguridad: la deduplicación de un usuario nunca puede coincidir contra los
    /// movimientos de otro, ni siquiera cuando ambos importan un archivo idéntico.
    /// El filtro explícito por UserId se mantiene delante de `WhereIdIn`
    /// (backend/src/Nexo.Application/Deduplication/DeduplicationService.cs) --
    /// esta prueba demuestra que el arreglo no lo debilitó.
    /// </summary>
    [Fact]
    public async Task Deduplication_never_matches_across_users_even_with_identical_statements()
    {
        var userA = await factory.RegisterUserAsync();
        var accountA = await userA.CreateAccountAsync(openingBalance: null);
        var importA = await userA.UploadStatementAsync(accountA, Import1ThreeNewMovements);
        await userA.ConfirmImportAsync(importA.GetProperty("importId").GetGuid());

        var userB = await factory.RegisterUserAsync();
        var accountB = await userB.CreateAccountAsync(openingBalance: null);

        // Same dates, same amounts, same descriptions as User A's confirmed import.
        // If dedup ever looked across users, this would come back as 3 duplicates.
        var importB = await userB.UploadStatementAsync(accountB, Import1ThreeNewMovements);

        Assert.Equal(3, importB.GetProperty("newRows").GetInt32());
        Assert.Equal(0, importB.GetProperty("duplicateRows").GetInt32());

        await userB.ConfirmImportAsync(importB.GetProperty("importId").GetGuid());

        var userBTransactions = await userB.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(3, userBTransactions.GetProperty("totalCount").GetInt32());

        // And User A's own ledger is untouched by User B's identical import.
        var userATransactions = await userA.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(3, userATransactions.GetProperty("totalCount").GetInt32());
    }
}
