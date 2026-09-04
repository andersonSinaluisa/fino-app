using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Imports.Parsing;

/// <summary>Everything a parser is allowed to look at. Never a bank-specific type.</summary>
public sealed class StatementFileContext
{
    public StatementFileContext(
        string fileName,
        string contentType,
        TabularTable table,
        string? providerCodeHint = null,
        StatementDateInterpreter? dates = null)
    {
        FileName = fileName;
        ContentType = contentType;
        Table = table;
        ProviderCodeHint = providerCodeHint;
        Dates = dates ?? StatementDateInterpreter.Ecuador();
    }

    public string FileName { get; }

    public string ContentType { get; }

    public TabularTable Table { get; }

    /// <summary>Provider the account is attached to. A hint, not a guarantee.</summary>
    public string? ProviderCodeHint { get; }

    public StatementDateInterpreter Dates { get; }

    /// <summary>
    /// Upper-cased text of the rows above the header — the bank's own letterhead.
    /// Signature detection must not read the movements: a statement containing
    /// "EMPRESA ELECTRICA GUAYAQUIL" is not a Banco Guayaquil export.
    /// </summary>
    public string HeadingText(int headerRowIndex)
    {
        var take = headerRowIndex <= 0 ? 0 : Math.Min(headerRowIndex, 15);

        return take == 0
            ? string.Empty
            : string.Join(" \n ", Table.Rows.Take(take).Select(r => r.Join())).ToUpperInvariant();
    }
}

/// <summary>One movement as the parser understood it, before it becomes a Transaction.</summary>
public sealed record ParsedStatementTransaction(
    int RowNumber,
    DateTimeOffset TransactionDate,
    decimal Amount,
    TransactionDirection Direction,
    string Description,
    string? ExternalReference,
    decimal? RunningBalance,
    string? RawPayload);

public sealed record StatementRowError(int RowNumber, string Message, string? RawPayload);

public sealed class StatementParseResult
{
    private StatementParseResult(string parserCode, bool succeeded)
    {
        ParserCode = parserCode;
        Succeeded = succeeded;
    }

    public string ParserCode { get; }

    public bool Succeeded { get; }

    public string? FailureReason { get; private init; }

    public IReadOnlyList<ParsedStatementTransaction> Transactions { get; private init; } = [];

    public IReadOnlyList<StatementRowError> Errors { get; private init; } = [];

    public string? DetectedProviderCode { get; private init; }

    public string? AccountMask { get; private init; }

    public DateTimeOffset? PeriodStart { get; private init; }

    public DateTimeOffset? PeriodEnd { get; private init; }

    public decimal? DeclaredClosingBalance { get; private init; }

    public static StatementParseResult Success(
        string parserCode,
        IReadOnlyList<ParsedStatementTransaction> transactions,
        IReadOnlyList<StatementRowError> errors,
        string? detectedProviderCode = null,
        string? accountMask = null,
        decimal? declaredClosingBalance = null) =>
        new(parserCode, true)
        {
            Transactions = transactions,
            Errors = errors,
            DetectedProviderCode = detectedProviderCode,
            AccountMask = accountMask,
            DeclaredClosingBalance = declaredClosingBalance,
            PeriodStart = transactions.Count > 0 ? transactions.Min(t => t.TransactionDate) : null,
            PeriodEnd = transactions.Count > 0 ? transactions.Max(t => t.TransactionDate) : null,
        };

    public static StatementParseResult Failure(string parserCode, string reason) =>
        new(parserCode, false) { FailureReason = reason };
}

/// <summary>
/// The extension point for statement files. Adding a bank means adding one class;
/// nothing in the import pipeline knows any bank by name.
/// </summary>
public interface IStatementParser
{
    /// <summary>Stable identifier persisted on the import, e.g. "PICHINCHA_V1".</summary>
    string ParserCode { get; }

    /// <summary>Provider this parser produces movements for, when it is bank-specific.</summary>
    string? ProviderCode { get; }

    /// <summary>Lower runs first. Bank-specific parsers must beat the generic one.</summary>
    int Priority { get; }

    bool CanParse(StatementFileContext context);

    Task<StatementParseResult> ParseAsync(StatementFileContext context, CancellationToken cancellationToken);
}

public interface IStatementParserResolver
{
    IStatementParser? Resolve(StatementFileContext context);

    IReadOnlyList<IStatementParser> All { get; }
}
