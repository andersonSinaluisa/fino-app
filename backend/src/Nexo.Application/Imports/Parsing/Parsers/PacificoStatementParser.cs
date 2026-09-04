using Nexo.Domain.Providers;

namespace Nexo.Application.Imports.Parsing.Parsers;

/// <summary>Banco del Pacífico statement export (CSV/XLSX).</summary>
public sealed class PacificoStatementParser : HeaderMappedStatementParser
{
    public override string ParserCode => "PACIFICO_V1";

    public override string? ProviderCode => ProviderCodes.Pacifico;

    public override int Priority => 10;

    protected override IReadOnlyList<string> FileSignatures =>
        ["PACIFICO", "BANCO DEL PACIFICO"];

    protected override IReadOnlyList<string> DateHeaders =>
        ["FECHA", "FECHA DE TRANSACCION"];

    protected override IReadOnlyList<string> DescriptionHeaders =>
        ["DESCRIPCION", "CONCEPTO", "DETALLE", "TRANSACCION"];

    protected override IReadOnlyList<string> AmountHeaders =>
        ["VALOR", "MONTO", "IMPORTE"];

    protected override IReadOnlyList<string> DebitHeaders =>
        ["DEBITO", "CARGO"];

    protected override IReadOnlyList<string> CreditHeaders =>
        ["CREDITO", "ABONO"];

    protected override IReadOnlyList<string> ReferenceHeaders =>
        ["REFERENCIA", "DOCUMENTO"];
}
