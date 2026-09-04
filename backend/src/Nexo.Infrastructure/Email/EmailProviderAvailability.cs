using Microsoft.Extensions.Options;
using Nexo.Application.EmailIngestion;
using Nexo.Domain.EmailIngestion;

namespace Nexo.Infrastructure.Email;

public sealed class EmailIngestionOptions
{
    public const string SectionName = "Nexo:EmailIngestion";

    public OAuthClientOptions Gmail { get; set; } = new();

    public OAuthClientOptions Outlook { get; set; } = new();

    /// <summary>Inbound forwarding needs a mail domain and a webhook secret, not OAuth.</summary>
    public ForwardingOptions Forwarding { get; set; } = new();
}

public sealed class OAuthClientOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !string.IsNullOrWhiteSpace(RedirectUri);
}

public sealed class ForwardingOptions
{
    public string InboundDomain { get; set; } = string.Empty;

    public string WebhookSecret { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(InboundDomain) && !string.IsNullOrWhiteSpace(WebhookSecret);
}

/// <summary>
/// Reports what is actually wired up in this environment. With no credentials the
/// answer is "not available" — the app then hides the option instead of offering a
/// connect button that cannot work.
/// </summary>
public sealed class ConfigurationEmailProviderAvailability(IOptions<EmailIngestionOptions> options)
    : IEmailProviderAvailability
{
    private readonly EmailIngestionOptions _options = options.Value;

    public bool IsConfigured(EmailProviderKind kind) => kind switch
    {
        EmailProviderKind.Gmail => _options.Gmail.IsConfigured,
        EmailProviderKind.Outlook => _options.Outlook.IsConfigured,
        EmailProviderKind.Forwarding => _options.Forwarding.IsConfigured,
        _ => false,
    };
}
