using Nexo.Domain.Providers;

namespace Nexo.Application.Imports.Parsing.Parsers;

/// <summary>
/// Banco Pichincha statement export (CSV/XLSX).
/// Header synonyms below were derived from the exports we have seen; they are the
/// single place to adjust when a real customer export differs. The parser is
/// registered even if the synonyms need tuning, because the resolver falls back to
/// the generic parser rather than failing the import.
/// </summary>
public sealed class PichinchaStatementParser : HeaderMappedStatementParser
{
    public override string ParserCode => "PICHINCHA_V1";

    public override string? ProviderCode => ProviderCodes.Pichincha;

    public override int Priority => 10;

    protected override IReadOnlyList<string> FileSignatures =>
        ["PICHINCHA", "BANCO PICHINCHA"];

    protected override IReadOnlyList<string> DateHeaders =>
        ["FECHA", "FECHA TRANSACCION", "FECHA DE TRANSACCION"];

    protected override IReadOnlyList<string> DescriptionHeaders =>
        ["CONCEPTO", "DESCRIPCION", "DETALLE"];

    protected override IReadOnlyList<string> DebitHeaders =>
        ["DEBITO", "DEBITOS", "VALOR DEBITO"];

    protected override IReadOnlyList<string> CreditHeaders =>
        ["CREDITO", "CREDITOS", "VALOR CREDITO"];

    protected override IReadOnlyList<string> ReferenceHeaders =>
        ["DOCUMENTO", "NUMERO DOCUMENTO", "NRO DOCUMENTO", "REFERENCIA"];

    protected override IReadOnlyList<string> BalanceHeaders =>
        ["SALDO", "SALDO CONTABLE"];
}
