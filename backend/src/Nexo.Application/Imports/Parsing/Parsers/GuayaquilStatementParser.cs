using Nexo.Domain.Providers;

namespace Nexo.Application.Imports.Parsing.Parsers;

/// <summary>
/// Banco Guayaquil statement export. The bank ships two shapes in the wild: a
/// plain CSV/XLSX with Debito/Credito columns (the one this parser originally
/// targeted), and the real export from its personal-banking portal -- an HTML
/// document saved with a ".xls" extension, one signed "Monto" column plus a
/// "Tipo" (Ingreso/Egreso) column, and the counterparty named separately from
/// the channel ("Detalle" says "Banco Guayaquil"/"IVA"; "Beneficiario" says who).
/// <see cref="HeaderMappedStatementParser"/>'s column mapping already supports
/// either shape (Debit/Credit or Amount+Type) through the same synonym lists, so
/// one parser class covers both -- no bank-specific branching needed.
/// </summary>
public sealed class GuayaquilStatementParser : HeaderMappedStatementParser
{
    public override string ParserCode => "GUAYAQUIL_V1";

    public override string? ProviderCode => ProviderCodes.Guayaquil;

    public override int Priority => 10;

    protected override IReadOnlyList<string> FileSignatures =>
        ["BANCO GUAYAQUIL", "GUAYAQUIL"];

    // The real HTML export never mentions the bank's name in the rows above the
    // header (its branding is a CSS color, which HeadingText never sees) -- so
    // content-based detection needs the header row's own shape. "Beneficiario"
    // next to "Saldo efectivo" is a combination none of the other three banks'
    // exports produce, letting this parser claim the file without relying on the
    // account's provider hint alone.
    protected override IReadOnlyList<string> HeaderSignatures =>
        ["BENEFICIARIO", "SALDO EFECTIVO"];

    protected override IReadOnlyList<string> DateHeaders =>
        ["FECHA", "FECHA CONTABLE", "FECHA MOVIMIENTO"];

    protected override IReadOnlyList<string> DescriptionHeaders =>
        ["DESCRIPCION", "CONCEPTO", "DETALLE MOVIMIENTO", "DETALLE"];

    protected override IReadOnlyList<string> DebitHeaders =>
        ["DEBITO", "CARGO", "VALOR DEBITO"];

    protected override IReadOnlyList<string> CreditHeaders =>
        ["CREDITO", "ABONO", "VALOR CREDITO"];

    protected override IReadOnlyList<string> ReferenceHeaders =>
        ["REFERENCIA", "DOCUMENTO", "COMPROBANTE"];
}
