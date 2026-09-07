using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Abstractions;
using Nexo.Domain.Common;
using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Entregable 27 ("Dedup correo/importación"): before this, a probable duplicate
/// was flagged <see cref="TransactionStatus.NeedsReview"/> and the mobile detail
/// screen told the person "lo guardamos aparte para que decidas tú" -- but no
/// endpoint existed to record that decision, so it stayed flagged forever
/// (<c>Transaction.ConfirmNotDuplicate</c> and <c>Transaction.Ignore</c> had zero
/// callers anywhere in the application). <c>PUT /transactions/{id}/duplicate-review</c>
/// closes that loop.
/// </summary>
public class DuplicateReviewTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private const string StatementRestatingTheEmailedMovement = """
        Banco Pichincha
        Fecha,Concepto,Documento,Debito,Credito
        05/03/2026,SUPERMAXI ALBORADA GYE,,48.20,
        """;

    /// <summary>Seeds an emailed movement and imports a statement that restates it as a ProbableMatch, exactly like CrossSourceDeduplicationTests.</summary>
    private static async Task<(string TransactionId, Guid AccountId, TestUser User)> SeedNeedsReviewTransactionAsync(NexoApiFactory factory)
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<INexoDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            var emailed = Transaction.Create(
                userId: user.Id,
                financialAccountId: accountId,
                providerCode: "PICHINCHA",
                transactionDate: new DateTimeOffset(2026, 3, 4, 23, 12, 0, TimeSpan.Zero),
                amount: 48.20m,
                direction: TransactionDirection.Expense,
                description: "Compra en SUPERMAXI ALBORADA con tu tarjeta terminada en 4821",
                source: TransactionSource.Email,
                now: clock.UtcNow,
                confidence: SourceConfidence.Medium,
                status: TransactionStatus.Pending);

            db.Transactions.Add(emailed);
            await db.SaveChangesAsync(CancellationToken.None);
        }

        var preview = await user.UploadStatementAsync(accountId, StatementRestatingTheEmailedMovement);
        await user.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());

        var transactions = await user.GetJsonAsync("/api/v1/transactions");
        var flagged = transactions.GetProperty("items")
            .EnumerateArray()
            .First(t => t.GetProperty("status").GetString() == "NeedsReview");

        return (flagged.GetProperty("id").GetString()!, accountId, user);
    }

    [Fact]
    public async Task Keeping_it_as_a_separate_movement_posts_it_and_counts_it_towards_the_balance()
    {
        var (transactionId, accountId, user) = await SeedNeedsReviewTransactionAsync(factory);

        var before = await user.GetJsonAsync($"/api/v1/accounts/{accountId}");
        var balanceBeforeResolution = before.GetProperty("balance").GetDecimal();

        var response = await user.Client.PutAsJsonAsync(
            $"/api/v1/transactions/{transactionId}/duplicate-review",
            new { keepAsSeparate = true });
        await response.EnsureOkAsync();

        var resolved = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Posted", resolved.GetProperty("status").GetString());

        // NeedsReview never counted towards the balance; Posted does -- so
        // resolving it this way must move the account's balance.
        var after = await user.GetJsonAsync($"/api/v1/accounts/{accountId}");
        Assert.NotEqual(balanceBeforeResolution, after.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task Agreeing_it_is_the_same_movement_ignores_it_without_deleting_it()
    {
        var (transactionId, _, user) = await SeedNeedsReviewTransactionAsync(factory);

        var response = await user.Client.PutAsJsonAsync(
            $"/api/v1/transactions/{transactionId}/duplicate-review",
            new { keepAsSeparate = false });
        await response.EnsureOkAsync();

        var resolved = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Ignored", resolved.GetProperty("status").GetString());

        // Never deleted: still reachable by id, still visible with includeIgnored=true.
        var stillThere = await user.GetJsonAsync($"/api/v1/transactions/{transactionId}");
        Assert.Equal("Ignored", stillThere.GetProperty("status").GetString());

        var withIgnored = await user.GetJsonAsync("/api/v1/transactions?includeIgnored=true");
        Assert.Contains(
            withIgnored.GetProperty("items").EnumerateArray(),
            t => t.GetProperty("id").GetString() == transactionId);
    }

    [Fact]
    public async Task A_movement_that_was_never_flagged_cannot_be_resolved()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);
        var confirmed = await user.ConfirmAndListAsync(
            accountId,
            """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            01/03/2026,SUPERMAXI ALBORADA,TRX-1,20.00,
            """);

        var response = await user.Client.PutAsJsonAsync(
            $"/api/v1/transactions/{confirmed[0].GetProperty("id").GetGuid()}/duplicate-review",
            new { keepAsSeparate = true });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_user_cannot_resolve_another_users_duplicate_flag()
    {
        var (transactionId, _, _) = await SeedNeedsReviewTransactionAsync(factory);
        var intruder = await factory.RegisterUserAsync();

        var response = await intruder.Client.PutAsJsonAsync(
            $"/api/v1/transactions/{transactionId}/duplicate-review",
            new { keepAsSeparate = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
