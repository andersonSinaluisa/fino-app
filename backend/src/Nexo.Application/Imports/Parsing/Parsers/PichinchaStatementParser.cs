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

    /// <summary>
    /// The XLSX export from banca web carries no bank name anywhere — its heading
    /// says only "Movimientos de Cuenta". This header combination is what actually
    /// identifies it, and it is specific enough not to catch other banks.
    /// </summary>
    protected override IReadOnlyList<string> HeaderSignatures =>
        ["FECHA", "CONCEPTO", "NRO DOCUMENTO", "TIPO", "BENEFICIARIO", "MONTO", "SALDO"];

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

    // The export writes the amount signed in a single "Monto" column and repeats
    // the direction in "Tipo"; there are no debit/credit columns.
    protected override IReadOnlyList<string> AmountHeaders =>
        ["MONTO", "VALOR", "IMPORTE"];
}
