namespace Nexo.Domain.Common;

/// <summary>
/// Raised when an operation would leave an aggregate in an invalid state.
/// Mapped to HTTP 422 by the API error handler.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }

    public DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; } = "domain_rule_violated";

    public static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new DomainException(message);
        }
    }

    public static string RequireText(string? value, string field, int maxLength = 512)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"'{field}' is required.");
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
