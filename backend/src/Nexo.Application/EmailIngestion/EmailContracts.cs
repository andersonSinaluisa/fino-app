using Nexo.Domain.Transactions;

namespace Nexo.Application.EmailIngestion;

/// <summary>
/// A message as the ingestion layer sees it. Deliberately provider-agnostic: Gmail,
/// Microsoft Graph and inbound forwarding all normalise into this shape.
/// <paramref name="PassedAuthentication"/> reflects SPF/DKIM/DMARC as evaluated by
/// the upstream mail provider — Nexo never re-implements mail authentication, it
/// just refuses to trust a message that did not pass it.
/// </summary>
public sealed record EmailMessage(
    string MessageId,
    string EnvelopeFrom,
    string? FromDisplayName,
    string Subject,
    string PlainTextBody,
    DateTimeOffset ReceivedAt,
    bool PassedAuthentication)
{
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Subject and body together, which is what the patterns run against.
    /// Entregable 26 ("Parsers de correo"): nothing in this pipeline decodes HTML
    /// entities, and there is no guarantee a relay always hands over clean plain
    /// text -- an undecoded "SUPERMAXI&amp;nbsp;ALBORADA" would otherwise poison
    /// the merchant capture with a literal "Nbsp" token once normalized. Decoding
    /// once, here, fixes every downstream regex/normalizer at the same time; it is
    /// a no-op on text that had no entities to begin with.
    /// </summary>
    public string SearchableText => System.Net.WebUtility.HtmlDecode($"{Subject}\n{PlainTextBody}");
}

/// <summary>What a bank email parser produces. Not yet a domain Transaction.</summary>
public sealed record ParsedTransaction(
    string ProviderCode,
    decimal Amount,
    TransactionDirection Direction,
    DateTimeOffset OccurredAt,
    string Description,
    string? Merchant,
    string? AccountMask,
    string? ExternalReference,
    SourceConfidence Confidence);

/// <summary>
/// One parser per bank notification format. Adding a bank is adding a class and a
/// DI registration; nothing else in the pipeline changes.
/// </summary>
public interface IBankEmailParser
{
    string ProviderCode { get; }

    string ParserCode { get; }

    int Priority { get; }

    bool CanParse(EmailMessage message);

    Task<ParsedTransaction?> ParseAsync(EmailMessage message, CancellationToken cancellationToken);
}

public interface IBankEmailParserResolver
{
    IBankEmailParser? Resolve(EmailMessage message, string? providerCodeHint);

    IReadOnlyList<IBankEmailParser> All { get; }
}

public enum SenderValidationOutcome
{
    Accepted = 0,

    /// <summary>Sender domain is not on the allow-list for any provider.</summary>
    UnknownSender = 1,

    /// <summary>Sender matches a bank domain but the message failed SPF/DKIM/DMARC.</summary>
    FailedAuthentication = 2,
}

public sealed record SenderValidationResult(SenderValidationOutcome Outcome, string? ProviderCode)
{
    public bool IsAccepted => Outcome == SenderValidationOutcome.Accepted;
}

public interface ISenderValidator
{
    Task<SenderValidationResult> ValidateAsync(EmailMessage message, CancellationToken cancellationToken);
}

public enum EmailIngestionOutcome
{
    /// <summary>Sender not trusted, or authentication failed. Nothing was stored.</summary>
    Rejected = 0,

    /// <summary>Trusted sender but no parser recognised the message (e.g. a marketing email).</summary>
    NotAMovement = 1,

    /// <summary>Parsed, but the movement already exists.</summary>
    Duplicate = 2,

    /// <summary>Parsed and stored as a pending movement.</summary>
    Created = 3,

    /// <summary>Parsed but no account of the user matches the provider/mask.</summary>
    NoMatchingAccount = 4,
}

public sealed record EmailIngestionResult(
    EmailIngestionOutcome Outcome,
    Guid? TransactionId = null,
    string? ProviderCode = null,
    string? Reason = null);
