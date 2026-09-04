using Nexo.Domain.Common;

namespace Nexo.Domain.EmailIngestion;

/// <summary>
/// Allow-list of sender addresses and domains per provider. A message is only
/// considered for parsing when its envelope sender matches an entry here AND the
/// message passed authentication (SPF/DKIM/DMARC) upstream — the display name is
/// never trusted, because faking "Banco Pichincha" in a From header is trivial.
/// </summary>
public sealed class TrustedSender : Entity
{
    private TrustedSender()
    {
    }

    public string ProviderCode { get; private set; } = null!;

    /// <summary>Domain such as "pichincha.com" or a full address.</summary>
    public string Value { get; private set; } = null!;

    public bool IsDomain { get; private set; }

    /// <summary>When true, the message must pass DMARC to be accepted.</summary>
    public bool RequireAuthenticated { get; private set; } = true;

    public bool IsActive { get; private set; } = true;

    public static TrustedSender Domain(string providerCode, string domain, DateTimeOffset now)
    {
        var sender = new TrustedSender
        {
            ProviderCode = DomainException.RequireText(providerCode, nameof(providerCode), 40).ToUpperInvariant(),
            Value = DomainException.RequireText(domain, nameof(domain), 200).ToLowerInvariant().TrimStart('@'),
            IsDomain = true,
        };
        sender.Stamp(now);
        return sender;
    }

    public static TrustedSender Address(string providerCode, string address, DateTimeOffset now)
    {
        var sender = new TrustedSender
        {
            ProviderCode = DomainException.RequireText(providerCode, nameof(providerCode), 40).ToUpperInvariant(),
            Value = DomainException.RequireText(address, nameof(address), 200).ToLowerInvariant(),
            IsDomain = false,
        };
        sender.Stamp(now);
        return sender;
    }

    public bool Accepts(string envelopeFrom, bool authenticated)
    {
        if (!IsActive || string.IsNullOrWhiteSpace(envelopeFrom))
        {
            return false;
        }

        if (RequireAuthenticated && !authenticated)
        {
            return false;
        }

        var address = envelopeFrom.Trim().ToLowerInvariant();

        if (!IsDomain)
        {
            return string.Equals(address, Value, StringComparison.Ordinal);
        }

        var at = address.LastIndexOf('@');
        if (at < 0 || at == address.Length - 1)
        {
            return false;
        }

        var domain = address[(at + 1)..];
        return string.Equals(domain, Value, StringComparison.Ordinal)
            || domain.EndsWith('.' + Value, StringComparison.Ordinal);
    }
}
