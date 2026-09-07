using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Domain.Common;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Entregable 8: GET /api/v1/imports is what the mobile "Historial de
/// importaciones" screen renders, and it is also how we can prove -- at the
/// API level, without a device -- that nothing is left orphaned: every import
/// a user starts must show up here with its real, current status (including
/// Cancelled), for every account, newest first, and never another user's.
/// </summary>
public class ImportHistoryTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    private const string Csv = """
        Fecha,Concepto,Debito,Credito
        04/03/2026,PAGO SERVICIOS,25.50,
        """;

    [Fact]
    public async Task Listing_shows_every_import_for_every_account_of_the_user_newest_first()
    {
        var user = await factory.RegisterUserAsync();
        var accountA = await user.CreateAccountAsync(openingBalance: null);
        var accountB = await user.CreateAccountAsync(openingBalance: null);

        // NexoApiFactory pins IClock to one fixed instant so every other test's
        // timestamps are predictable; two uploads made back to back would then
        // tie on CreatedAt and "newest first" would be unverifiable. Advance the
        // same clock the app itself reads, exactly like a real device would just
        // by uploading a minute apart, and restore it so no other test in this
        // class inherits the shift.
        using var scope = factory.Services.CreateScope();
        var clock = (FixedClock)scope.ServiceProvider.GetRequiredService<IClock>();
        var originalNow = clock.UtcNow;

        System.Text.Json.JsonElement first;
        System.Text.Json.JsonElement second;
        try
        {
            first = await user.UploadStatementAsync(accountA, Csv, fileName: "primero.csv");
            clock.UtcNow = originalNow.AddMinutes(1);
            second = await user.UploadStatementAsync(accountB, Csv, fileName: "segundo.csv");
        }
        finally
        {
            clock.UtcNow = originalNow;
        }

        var history = await user.GetJsonAsync("/api/v1/imports");

        Assert.Equal(2, history.GetProperty("totalCount").GetInt32());
        var items = history.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());

        // Newest first: "segundo.csv" was uploaded after "primero.csv".
        Assert.Equal("segundo.csv", items[0].GetProperty("fileName").GetString());
        Assert.Equal("primero.csv", items[1].GetProperty("fileName").GetString());

        Assert.Equal(second.GetProperty("importId").GetGuid(), items[0].GetProperty("importId").GetGuid());
        Assert.Equal(accountB, items[0].GetProperty("financialAccountId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(items[0].GetProperty("accountAlias").GetString()));
        Assert.Equal("PreviewReady", items[0].GetProperty("status").GetString());
        Assert.Equal(1, items[0].GetProperty("newRows").GetInt32());
        Assert.Equal(0, items[0].GetProperty("duplicateRows").GetInt32());
        Assert.Equal(0, items[0].GetProperty("invalidRows").GetInt32());
        Assert.Equal(0, items[0].GetProperty("importedCount").GetInt32());

        _ = first;
    }

    [Fact]
    public async Task A_cancelled_import_shows_up_as_cancelled_never_stuck_pending()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var preview = await user.UploadStatementAsync(accountId, Csv);
        var importId = preview.GetProperty("importId").GetGuid();

        var cancelResponse = await user.Client.DeleteAsync($"/api/v1/imports/{importId}");
        Assert.Equal(HttpStatusCode.NoContent, cancelResponse.StatusCode);

        var history = await user.GetJsonAsync("/api/v1/imports");
        var item = history.GetProperty("items")[0];

        Assert.Equal(importId, item.GetProperty("importId").GetGuid());
        Assert.Equal("Cancelled", item.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_confirmed_import_reports_its_final_imported_count()
    {
        var user = await factory.RegisterUserAsync();
        var accountId = await user.CreateAccountAsync(openingBalance: null);

        var preview = await user.UploadStatementAsync(accountId, Csv);
        await user.ConfirmImportAsync(preview.GetProperty("importId").GetGuid());

        var history = await user.GetJsonAsync("/api/v1/imports");
        var item = history.GetProperty("items")[0];

        Assert.Equal("Completed", item.GetProperty("status").GetString());
        Assert.Equal(1, item.GetProperty("importedCount").GetInt32());
    }

    [Fact]
    public async Task A_users_import_history_never_shows_another_users_imports()
    {
        var owner = await factory.RegisterUserAsync();
        var accountId = await owner.CreateAccountAsync(openingBalance: null);
        await owner.UploadStatementAsync(accountId, Csv);

        var stranger = await factory.RegisterUserAsync();
        var strangerHistory = await stranger.GetJsonAsync("/api/v1/imports");

        Assert.Equal(0, strangerHistory.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, strangerHistory.GetProperty("items").GetArrayLength());
    }
}
