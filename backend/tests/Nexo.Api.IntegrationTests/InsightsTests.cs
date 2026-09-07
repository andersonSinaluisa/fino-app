using Microsoft.Extensions.DependencyInjection;
using Nexo.Domain.Common;
using Xunit;

namespace Nexo.Api.IntegrationTests;

/// <summary>
/// Entregable 16 ("Insights v1"): the rules-engine insights already computed by
/// InsightEngine (gasto vs. mes anterior, comercio frecuente, cuenta
/// desactualizada, ingresos vs. gastos, etc.) at the API level, plus the new
/// guarantee that an insight stops showing once <c>ValidUntil</c> passes even
/// without a fresh recompute -- "no generar insights irrelevantes" applies over
/// time, not only at the moment of computing.
/// </summary>
public class InsightsTests(NexoApiFactory factory) : IClassFixture<NexoApiFactory>
{
    [Fact]
    public async Task Real_data_produces_the_expected_insight_codes_with_their_thresholds_applied()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: null);
        // Never touched -- InsightEngine evaluates staleness across every one of
        // the user's manual-import accounts, not only the one just imported into.
        await user.CreateAccountAsync(openingBalance: null);

        var csv = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            15/02/2026,MI COMISARIATO,TRX-9001,40.00,
            01/03/2026,KFC LOCAL NORTE,TRX-9002,12.00,
            03/03/2026,KFC LOCAL NORTE,TRX-9003,13.00,
            05/03/2026,KFC LOCAL NORTE,TRX-9004,14.00,
            02/03/2026,ROL DE PAGOS,TRX-9005,,500.00
            """;
        await user.ConfirmImportAsync((await user.UploadStatementAsync(account, csv)).GetProperty("importId").GetGuid());

        var insights = await user.GetJsonAsync("/api/v1/insights");
        var codes = insights.EnumerateArray().Select(i => i.GetProperty("code").GetString()).ToHashSet();

        // gasto vs. mes anterior (hubo gasto en febrero y en marzo).
        Assert.Contains("MONTH_OVER_MONTH", codes);
        // comercio frecuente (KFC, 3 compras este mes -- el umbral mínimo).
        Assert.Contains("FREQUENT_MERCHANT", codes);
        // ingresos vs. gastos (hubo ambos este mes).
        Assert.Contains("INCOME_VS_EXPENSE", codes);
        // cuenta desactualizada (la segunda cuenta nunca se ha sincronizado).
        Assert.Contains("STALE_ACCOUNT", codes);

        var frequent = insights.EnumerateArray().First(i => i.GetProperty("code").GetString() == "FREQUENT_MERCHANT");
        Assert.Equal(39.00m, frequent.GetProperty("value").GetDecimal());

        // Every insight in this pass is scoped to March 2026, so ValidUntil must
        // be the instant the month rolls over -- never in the past, never open-ended.
        foreach (var insight in insights.EnumerateArray())
        {
            var validUntil = insight.GetProperty("validUntil").GetDateTimeOffset();
            Assert.True(validUntil > NexoApiFactory.Now);
        }
    }

    [Fact]
    public async Task An_insight_stops_showing_once_its_month_ends_even_without_a_fresh_recompute()
    {
        var user = await factory.RegisterUserAsync();
        var account = await user.CreateAccountAsync(openingBalance: null);

        var csv = """
            Banco Pichincha
            Fecha,Concepto,Documento,Debito,Credito
            15/02/2026,MI COMISARIATO,TRX-9101,40.00,
            05/03/2026,MI COMISARIATO,TRX-9102,60.00,
            """;
        await user.ConfirmImportAsync((await user.UploadStatementAsync(account, csv)).GetProperty("importId").GetGuid());

        var beforeRollover = await user.GetJsonAsync("/api/v1/insights");
        Assert.True(beforeRollover.GetArrayLength() > 0);

        // Move the same clock the app reads past the start of April -- a new
        // month began, but nothing triggered InsightEngine.RecomputeAsync again
        // (no new import, no worker pass in this test). The March insights must
        // disappear anyway, purely from the ValidUntil guard both readers apply.
        using var scope = factory.Services.CreateScope();
        var clock = (FixedClock)scope.ServiceProvider.GetRequiredService<IClock>();
        var originalNow = clock.UtcNow;

        System.Text.Json.JsonElement afterRollover;
        try
        {
            clock.UtcNow = originalNow.AddDays(25);
            afterRollover = await user.GetJsonAsync("/api/v1/insights");
        }
        finally
        {
            clock.UtcNow = originalNow;
        }

        Assert.Equal(0, afterRollover.GetArrayLength());
    }
}
