using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Abstractions;
using Nexo.Domain.Common;
using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Nexo's canonical model is shared by every channel (CSV, XLSX, email, API,
/// webhook — see Transaction's own doc comment), and
/// <c>DeduplicationService.CheckBatchAsync</c> never filters its candidate query by
/// <c>Source</c>: it looks at every posted-or-pending movement for the account in
/// the date window, whatever channel it arrived through. So a movement Nexo already
/// knows about from one channel must be recognised when it shows up again through a
/// different one — the textbook case being an email notification that arrives
/// before the bank statement does.
///
/// Full inbox-to-import dedup needs a connected mailbox, which is blocked on OAuth
/// credentials (Entregable 24/27). This test proves the same property without one:
/// it seeds a transaction exactly as <c>EmailIngestionPipeline</c> would persist it
/// (<c>TransactionSource.Email</c>, no bank reference, the shape a real email
/// notification produces) directly through <c>INexoDbContext</c>, then imports a
/// statement that restates the same movement, and checks it is not duplicated.
/// </summary>
public class CrossSourceDeduplicationTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private const string StatementRestatingTheEmailedMovement = """
        Banco Pichincha
        Fecha,Concepto,Documento,Debito,Credito
        05/03/2026,NETFLIX.COM,TRX-5001,12.99,
        06/03/2026,UBER TRIP,TRX-5002,6.80,
        """;

    [Fact]
    public async Task A_movement_already_known_from_email_is_not_duplicated_when_the_statement_arrives()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        // Simulates what EmailIngestionPipeline would have already written: same
        // account, same real-world movement, no bank reference (a notification
        // email rarely carries the statement's own document number), High
        // confidence because the sender passed authentication.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<INexoDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            var emailed = Transaction.Create(
                userId: user.Id,
                financialAccountId: accountId,
                providerCode: "PICHINCHA",
                transactionDate: new DateTimeOffset(2026, 3, 5, 12, 0, 0, TimeSpan.Zero),
                amount: 12.99m,
                direction: TransactionDirection.Expense,
                description: "NETFLIX.COM",
                source: TransactionSource.Email,
                now: clock.UtcNow,
                confidence: SourceConfidence.High);

            db.Transactions.Add(emailed);
            await db.SaveChangesAsync(CancellationToken.None);
        }

        // The statement later restates the same Netflix charge (plus one movement
        // that really is new) -- exactly what arrives a few days after the email.
        //
        // The emailed movement has no bank reference, so this can never hit the
        // "same_bank_reference" ExactMatch rule (it requires a usable reference on
        // both sides -- see DeduplicationMatcher), and the fingerprint recipe folds
        // reference presence in, so the fingerprints differ too. It falls through to
        // the amount+date+description heuristic: same day, same amount, identical
        // description -> score 1.0, well above the 0.72 threshold. This is a
        // ProbableMatch, not an ExactMatch -- ImportService counts the two
        // separately (`duplicates` vs `probable`, see ImportService.UploadAsync).
        var preview = await user.UploadStatementAsync(accountId, StatementRestatingTheEmailedMovement);

        Assert.Equal(1, preview.GetProperty("newRows").GetInt32());
        Assert.Equal(0, preview.GetProperty("duplicateRows").GetInt32());
        Assert.Equal(1, preview.GetProperty("probableDuplicateRows").GetInt32());

        // A probable match is never silently dropped: ImportService.ConfirmAsync
        // still writes it as a Transaction, just flagged via
        // FlagAsPossibleDuplicate (PossibleDuplicateOfId) for the user to review --
        // the hard rule that doubtful matches never erase information on their own.
        // So after confirm there are three rows: the email-seeded one, the new Uber
        // charge, and the Netflix charge imported-but-flagged.
        await user.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());

        var transactions = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(3, transactions.GetProperty("totalCount").GetInt32());
    }

    /// <summary>
    /// Entregable 27 ("Dedup correo/importación"): the exact-match twin of the test
    /// above. When the email row already carries the bank's own document number
    /// and the statement restates it with the same reference, provider, account,
    /// direction and amount, the reference-mode fingerprint recipe (which does not
    /// include the description -- see TransactionFingerprint.Compute) makes the two
    /// records collide on "same_fingerprint" regardless of wording, so this is an
    /// ExactMatch, not a ProbableMatch. ImportService.ConfirmAsync must fold the
    /// statement's authoritative data into the existing row via
    /// Transaction.UpgradeFrom, not just skip the statement row and leave the
    /// email's Pending/Medium row exactly as it was. Before this was wired up,
    /// this exact scenario silently threw the statement's data away.
    /// </summary>
    [Fact]
    public async Task An_exact_bank_reference_match_upgrades_the_emailed_row_instead_of_creating_a_second_one()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        Guid emailedId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<INexoDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            // The shape EmailIngestionPipeline actually produces: Pending status,
            // Medium confidence, a reference when the notification happens to
            // include the bank's own document number.
            var emailed = Transaction.Create(
                userId: user.Id,
                financialAccountId: accountId,
                providerCode: "PICHINCHA",
                transactionDate: new DateTimeOffset(2026, 3, 5, 12, 0, 0, TimeSpan.Zero),
                amount: 15.50m,
                direction: TransactionDirection.Expense,
                description: "Compra en FARMACIA CRUZ AZUL con tu tarjeta terminada en 4821",
                source: TransactionSource.Email,
                now: clock.UtcNow,
                externalReference: "TRX-6001",
                confidence: SourceConfidence.Medium,
                status: TransactionStatus.Pending);

            emailedId = emailed.Id;
            db.Transactions.Add(emailed);
            await db.SaveChangesAsync(CancellationToken.None);
        }

        const string statementWithMatchingReference = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            05/03/2026,FARMACIA CRUZ AZUL,TRX-6001,15.50,
            """;

        var preview = await user.UploadStatementAsync(accountId, statementWithMatchingReference);
        Assert.Equal(0, preview.GetProperty("newRows").GetInt32());
        Assert.Equal(1, preview.GetProperty("duplicateRows").GetInt32());
        Assert.Equal(0, preview.GetProperty("probableDuplicateRows").GetInt32());

        var result = await user.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());
        Assert.Equal(1, result.GetProperty("upgradedCount").GetInt32());

        // One real movement stays one row -- never a second one next to it.
        var transactions = await user.GetJsonAsync("/api/v1/transactions");
        Assert.Equal(1, transactions.GetProperty("totalCount").GetInt32());

        var upgraded = await user.GetJsonAsync($"/api/v1/transactions/{emailedId}");
        Assert.Equal("Posted", upgraded.GetProperty("status").GetString());
        Assert.Equal("High", upgraded.GetProperty("sourceConfidence").GetString());
        Assert.Equal("Import", upgraded.GetProperty("source").GetString());
        Assert.Equal("FARMACIA CRUZ AZUL", upgraded.GetProperty("description").GetString());
        Assert.Equal("TRX-6001", upgraded.GetProperty("externalReference").GetString());
    }
}
