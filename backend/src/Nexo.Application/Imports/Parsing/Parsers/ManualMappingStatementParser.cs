namespace Nexo.Application.Imports.Parsing.Parsers;

/// <summary>
/// Entregable 10: the generic CSV mapper. When no bank-specific or generic
/// parser recognises a file's headers, the user can tell Nexo by hand which
/// raw column is which. This class never appears in the resolver's list --
/// <see cref="ImportColumnMapping"/>'s indices are known only at the moment
/// the user (or a previously saved mapping) supplies them, so
/// <see cref="Imports.ImportService"/> constructs and calls one directly
/// instead of asking <see cref="StatementParserResolver"/> to find it.
///
/// It still extends <see cref="HeaderMappedStatementParser"/> so it gets
/// exactly the same row-walking, amount parsing, direction inference and
/// balance reconciliation as every bank-specific parser -- a manually mapped
/// file is held to the same correctness bar, not a lesser one.
/// </summary>
public sealed class ManualMappingStatementParser : HeaderMappedStatementParser
{
    private readonly StatementColumnMap _map;
    private readonly int _dataStartRow;

    public ManualMappingStatementParser(string providerCode, StatementColumnMap map, bool firstRowIsHeader)
    {
        ProviderCode = providerCode;
        _map = map;

        // ParseRows starts at headerIndex + 1; passing -1 here when there is no
        // header row makes that "row 0", not "row 1".
        _dataStartRow = firstRowIsHeader ? 0 : -1;
    }

    public override string ParserCode => "MANUAL_MAPPING_V1";

    public override string? ProviderCode { get; }

    public override int Priority => int.MaxValue;

    protected override IReadOnlyList<string> FileSignatures => [];

    // Never resolved automatically -- see the class doc comment.
    public override bool CanParse(StatementFileContext context) => false;

    public override Task<StatementParseResult> ParseAsync(StatementFileContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ParseRows(context, _dataStartRow, _map, cancellationToken);
    }
}
