using Microsoft.Extensions.Logging;

namespace Nexo.Application.EmailIngestion;

public sealed class BankEmailParserResolver(
    IEnumerable<IBankEmailParser> parsers,
    ILogger<BankEmailParserResolver> logger) : IBankEmailParserResolver
{
    private readonly List<IBankEmailParser> _parsers =
        parsers.OrderBy(p => p.Priority).ThenBy(p => p.ParserCode, StringComparer.Ordinal).ToList();

    public IReadOnlyList<IBankEmailParser> All => _parsers;

    public IBankEmailParser? Resolve(EmailMessage message, string? providerCodeHint)
    {
        // The provider identified by sender validation wins; the hint is never taken
        // from the message body, which an attacker controls.
        var ordered = providerCodeHint is null
            ? _parsers
            : _parsers
                .OrderBy(p => string.Equals(p.ProviderCode, providerCodeHint, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(p => p.Priority)
                .ToList();

        foreach (var parser in ordered)
        {
            if (providerCodeHint is not null
                && !string.Equals(parser.ProviderCode, providerCodeHint, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                if (parser.CanParse(message))
                {
                    return parser;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Email parser {ParserCode} threw while inspecting a message.", parser.ParserCode);
            }
        }

        return null;
    }
}
