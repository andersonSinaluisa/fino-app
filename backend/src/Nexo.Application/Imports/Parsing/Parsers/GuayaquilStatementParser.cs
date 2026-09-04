using Nexo.Domain.Providers;

namespace Nexo.Application.Imports.Parsing.Parsers;

/// <summary>Banco Guayaquil statement export (CSV/XLSX).</summary>
public sealed class GuayaquilStatementParser : HeaderMappedStatementParser
{
    public override string ParserCode => "GUAYAQUIL_V1";

    public override string? ProviderCode => ProviderCodes.Guayaquil;

    public override int Priority => 10;

    protected override IReadOnlyList<string> FileSignatures =>
        ["BANCO GUAYAQUIL", "GUAYAQUIL"];

    protected override IReadOnlyList<string> DateHeaders =>
        ["FECHA", "FECHA CONTABLE", "FECHA MOVIMIENTO"];

    protected override IReadOnlyList<string> DescriptionHeaders =>
        ["DESCRIPCION", "CONCEPTO", "DETALLE MOVIMIENTO"];

    protected override IReadOnlyList<string> DebitHeaders =>
        ["DEBITO", "CARGO", "VALOR DEBITO"];

    protected override IReadOnlyList<string> CreditHeaders =>
        ["CREDITO", "ABONO", "VALOR CREDITO"];

    protected override IReadOnlyList<string> ReferenceHeaders =>
        ["REFERENCIA", "DOCUMENTO", "COMPROBANTE"];
}
