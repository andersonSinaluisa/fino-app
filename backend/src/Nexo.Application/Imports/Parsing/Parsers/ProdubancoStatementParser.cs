using Nexo.Domain.Providers;

namespace Nexo.Application.Imports.Parsing.Parsers;

/// <summary>Produbanco statement export (CSV/XLSX).</summary>
public sealed class ProdubancoStatementParser : HeaderMappedStatementParser
{
    public override string ParserCode => "PRODUBANCO_V1";

    public override string? ProviderCode => ProviderCodes.Produbanco;

    public override int Priority => 10;

    protected override IReadOnlyList<string> FileSignatures =>
        ["PRODUBANCO", "GRUPO PROMERICA"];

    protected override IReadOnlyList<string> DateHeaders =>
        ["FECHA", "FECHA PROCESO", "FECHA TRANSACCION"];

    protected override IReadOnlyList<string> DescriptionHeaders =>
        ["DESCRIPCION", "CONCEPTO", "DETALLE"];

    protected override IReadOnlyList<string> DebitHeaders =>
        ["DEBITO", "DEBITOS", "EGRESO"];

    protected override IReadOnlyList<string> CreditHeaders =>
        ["CREDITO", "CREDITOS", "INGRESO"];

    protected override IReadOnlyList<string> ReferenceHeaders =>
        ["DOCUMENTO", "REFERENCIA", "NUMERO DOCUMENTO"];
}
