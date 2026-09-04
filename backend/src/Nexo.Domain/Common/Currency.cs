namespace Nexo.Domain.Common;

/// <summary>
/// Ecuador is fully dollarised, so the MVP works in USD only, but the currency is
/// stored per account and per transaction so multi-currency is a data change,
/// not a schema change.
/// </summary>
public static class Currency
{
    public const string Usd = "USD";

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Usd;
        }

        var code = value.Trim().ToUpperInvariant();
        if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
        {
            throw new DomainException("invalid_currency", $"'{value}' is not an ISO-4217 currency code.");
        }

        return code;
    }
}
