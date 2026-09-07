using Microsoft.Extensions.DependencyInjection;
using Nexo.Domain.Common;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Entregable 15 ("Dashboard final MVP"): GET /api/v1/summary must carry
/// "comparación mensual" and "cuentas desactualizadas" as guaranteed, always
/// computed figures -- unlike the MonthOverMonth / StaleAccount insights, which
/// only show up when they win one of the six "Para ti" card slots.
/// </summary>
public class DashboardSummaryTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    [Fact]
    public async Task Month_comparison_reflects_last_months_totals_against_this_months()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: null);

        var csv = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            20/02/2026,PAGO SERVICIOS,TRX-7001,100.00,
            20/02/2026,ROL DE PAGOS,TRX-7002,,500.00
            05/03/2026,MI COMISARIATO,TRX-7003,150.00,
            05/03/2026,ROL DE PAGOS,TRX-7004,,600.00
            """;
        await user.ConfirmImportAsync((await user.UploadStatementAsync(account, csv)).GetProperty("importId").GetGuid());

        var summary = await user.GetJsonAsync("/api/v1/summary");

        Assert.Equal(600.00m, summary.GetProperty("month").GetProperty("income").GetDecimal());
        Assert.Equal(150.00m, summary.GetProperty("month").GetProperty("expense").GetDecimal());

        var comparison = summary.GetProperty("monthComparison");
        Assert.Equal(500.00m, comparison.GetProperty("previousIncome").GetDecimal());
        Assert.Equal(100.00m, comparison.GetProperty("previousExpense").GetDecimal());
        // (600-500)/500*100 = 20% more income; (150-100)/100*100 = 50% more expense.
        Assert.Equal(20.0m, comparison.GetProperty("incomeChangePercent").GetDecimal());
        Assert.Equal(50.0m, comparison.GetProperty("expenseChangePercent").GetDecimal());
    }

    [Fact]
    public async Task With_nothing_in_the_previous_month_the_comparison_percentages_are_null_not_zero()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: null);

        var csv = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            05/03/2026,MI COMISARIATO,TRX-7101,80.00,
            """;
        await user.ConfirmImportAsync((await user.UploadStatementAsync(account, csv)).GetProperty("importId").GetGuid());

        var summary = await user.GetJsonAsync("/api/v1/summary");
        var comparison = summary.GetProperty("monthComparison");

        Assert.Equal(0m, comparison.GetProperty("previousIncome").GetDecimal());
        Assert.Equal(0m, comparison.GetProperty("previousExpense").GetDecimal());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, comparison.GetProperty("incomeChangePercent").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, comparison.GetProperty("expenseChangePercent").ValueKind);
    }

    [Fact]
    public async Task A_never_synced_account_shows_up_as_needing_an_update_immediately()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: null);

        var summary = await user.GetJsonAsync("/api/v1/summary");
        var stale = summary.GetProperty("staleAccounts");

        Assert.Equal(1, stale.GetArrayLength());
        Assert.Equal(account, stale[0].GetProperty("accountId").GetGuid());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, stale[0].GetProperty("lastSyncedAt").ValueKind);
    }

    [Fact]
    public async Task An_account_synced_moments_ago_is_not_stale()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: null);

        var csv = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            05/03/2026,MI COMISARIATO,TRX-7201,10.00,
            """;
        await user.ConfirmImportAsync((await user.UploadStatementAsync(account, csv)).GetProperty("importId").GetGuid());

        var summary = await user.GetJsonAsync("/api/v1/summary");
        Assert.Equal(0, summary.GetProperty("staleAccounts").GetArrayLength());
    }

    [Fact]
    public async Task An_account_not_synced_in_over_a_week_becomes_stale()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: null);

        var csv = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            05/03/2026,MI COMISARIATO,TRX-7301,10.00,
            """;
        await user.ConfirmImportAsync((await user.UploadStatementAsync(account, csv)).GetProperty("importId").GetGuid());

        // Right after syncing it is not stale -- confirmed above by the sibling
        // test. Move the same clock the app reads forward past the 7-day
        // threshold, exactly like a real week going by with no new import, then
        // restore it so no other test in this class inherits the shift.
        using var scope = factory.Services.CreateScope();
        var clock = (FixedClock)scope.ServiceProvider.GetRequiredService<IClock>();
        var originalNow = clock.UtcNow;

        System.Text.Json.JsonElement summary;
        try
        {
            clock.UtcNow = originalNow.AddDays(8);
            summary = await user.GetJsonAsync("/api/v1/summary");
        }
        finally
        {
            clock.UtcNow = originalNow;
        }

        var stale = summary.GetProperty("staleAccounts");
        Assert.Equal(1, stale.GetArrayLength());
        Assert.Equal(account, stale[0].GetProperty("accountId").GetGuid());
    }
}
