using Microsoft.Extensions.Logging;

namespace Nexo.Application.Imports.Parsing;

/// <summary>
/// Strategy resolution: the first parser (by priority) that claims the file wins.
/// Registering a new bank parser in DI is the whole integration story.
/// </summary>
public sealed class StatementParserResolver(
    IEnumerable<IStatementParser> parsers,
    ILogger<StatementParserResolver> logger) : IStatementParserResolver
{
    private readonly List<IStatementParser> _parsers =
        parsers.OrderBy(p => p.Priority).ThenBy(p => p.ParserCode, StringComparer.Ordinal).ToList();

    public IReadOnlyList<IStatementParser> All => _parsers;

    public IStatementParser? Resolve(StatementFileContext context)
    {
        // The parser for the account's own institution is tried first; after that,
        // priority order (bank-specific before generic).
        var ordered = context.ProviderCodeHint is null
            ? _parsers
            : _parsers
                .OrderBy(p => string.Equals(p.ProviderCode, context.ProviderCodeHint, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(p => p.Priority)
                .ThenBy(p => p.ParserCode, StringComparer.Ordinal)
                .ToList();

        foreach (var parser in ordered)
        {
            bool claims;
            try
            {
                claims = parser.CanParse(context);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A broken parser must never take the whole import down.
                logger.LogWarning(ex, "Statement parser {ParserCode} threw while inspecting a file.", parser.ParserCode);
                continue;
            }

            if (claims)
            {
                // Entregable 28 ("Observabilidad"): the file name is whatever the
                // user typed on their own computer -- it has shown up carrying a
                // name, a cédula, an account number. It never belongs in a log line
                // (docs/security.md's "no logueamos..." rule), and the parser code
                // alone is everything this line is for.
                logger.LogInformation("Statement parser {ParserCode} selected.", parser.ParserCode);
                return parser;
            }
        }

        return null;
    }
}
