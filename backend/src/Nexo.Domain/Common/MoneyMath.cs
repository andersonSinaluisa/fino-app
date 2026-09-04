namespace Nexo.Domain.Common;

public static class MoneyMath
{
    /// <summary>Bankers-safe rounding to cents. All persisted money is numeric(18,2).</summary>
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal Abs(decimal value) => Math.Abs(Round(value));

    public static decimal? PercentChange(decimal previous, decimal current)
    {
        if (previous == 0m)
        {
            return null;
        }

        return Math.Round((current - previous) / Math.Abs(previous) * 100m, 1, MidpointRounding.AwayFromZero);
    }
}
