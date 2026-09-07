using Nexo.Domain.Common;
using Nexo.Domain.Insights;
using Xunit;

namespace Nexo.Domain.Tests;

/// <summary>
/// Entregable 16 ("Insights v1"): every insight now carries a mandatory
/// <see cref="Insight.ValidUntil"/> so "no generar insights irrelevantes" holds
/// over time, not only at the moment InsightEngine computes it.
/// </summary>
public class InsightTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();

    [Fact]
    public void An_insight_cannot_be_created_already_expired()
    {
        var error = Assert.Throws<DomainException>(() => Insight.Create(
            User,
            InsightCodes.MonthlySpend,
            Now,
            Now,
            "Gasto de este mes",
            "Llevas $10.00 gastados este mes.",
            now: Now,
            validUntil: Now));

        Assert.Contains("vencido", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_insight_valid_in_the_future_is_created_normally()
    {
        var insight = Insight.Create(
            User,
            InsightCodes.MonthlySpend,
            Now,
            Now,
            "Gasto de este mes",
            "Llevas $10.00 gastados este mes.",
            now: Now,
            validUntil: Now.AddDays(20));

        Assert.Equal(Now.AddDays(20), insight.ValidUntil);
    }
}
