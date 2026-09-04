namespace Nexo.Application.Imports.Parsing.Parsers;

/// <summary>
/// Last-resort parser: any file with a recognisable header row works, whatever the
/// institution. This is what makes "any bank, today" true — a user with an
/// unsupported bank can still import as long as the export has date, description
/// and amount columns.
/// </summary>
public sealed class GenericStatementParser : HeaderMappedStatementParser
{
    public override string ParserCode => "GENERIC_V1";

    public override string? ProviderCode => null;

    public override int Priority => 1000;

    protected override IReadOnlyList<string> FileSignatures => [];

    public override bool CanParse(StatementFileContext context) =>
        context.Table.RowCount > 1 && TryFindHeader(context.Table, out _, out _);
}
