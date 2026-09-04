using Nexo.Domain.Providers;

namespace Nexo.Application.EmailIngestion.Parsers;

/// <summary>Banco Pichincha transactional notifications.</summary>
public sealed class PichinchaEmailParser : SpanishNotificationEmailParser
{
    public override string ProviderCode => ProviderCodes.Pichincha;

    public override string ParserCode => "PICHINCHA_EMAIL_V1";

    public override int Priority => 10;

    protected override IReadOnlyList<string> Signatures => ["PICHINCHA"];
}

/// <summary>Banco Guayaquil transactional notifications.</summary>
public sealed class GuayaquilEmailParser : SpanishNotificationEmailParser
{
    public override string ProviderCode => ProviderCodes.Guayaquil;

    public override string ParserCode => "GUAYAQUIL_EMAIL_V1";

    public override int Priority => 10;

    protected override IReadOnlyList<string> Signatures => ["GUAYAQUIL"];
}

/// <summary>Produbanco transactional notifications.</summary>
public sealed class ProdubancoEmailParser : SpanishNotificationEmailParser
{
    public override string ProviderCode => ProviderCodes.Produbanco;

    public override string ParserCode => "PRODUBANCO_EMAIL_V1";

    public override int Priority => 10;

    protected override IReadOnlyList<string> Signatures => ["PRODUBANCO"];
}

/// <summary>Banco del Pacífico transactional notifications.</summary>
public sealed class PacificoEmailParser : SpanishNotificationEmailParser
{
    public override string ProviderCode => ProviderCodes.Pacifico;

    public override string ParserCode => "PACIFICO_EMAIL_V1";

    public override int Priority => 10;

    protected override IReadOnlyList<string> Signatures => ["PACIFICO"];
}

/// <summary>DEUNA wallet notifications (transfers in and out).</summary>
public sealed class DeunaEmailParser : SpanishNotificationEmailParser
{
    public override string ProviderCode => ProviderCodes.Deuna;

    public override string ParserCode => "DEUNA_EMAIL_V1";

    public override int Priority => 10;

    protected override IReadOnlyList<string> Signatures => ["DEUNA"];

    protected override IReadOnlyList<string> IncomeKeywords =>
    [
        "RECIBISTE", "TRANSFERENCIA RECIBIDA", "TE ENVIO", "TE ENVIARON", "ACREDITACION", "DEPOSITO",
    ];

    protected override IReadOnlyList<string> ExpenseKeywords =>
    [
        "ENVIASTE", "TRANSFERENCIA ENVIADA", "PAGASTE", "PAGO REALIZADO", "COMPRA", "RETIRO",
    ];
}

/// <summary>PayPhone payment notifications.</summary>
public sealed class PayPhoneEmailParser : SpanishNotificationEmailParser
{
    public override string ProviderCode => ProviderCodes.PayPhone;

    public override string ParserCode => "PAYPHONE_EMAIL_V1";

    public override int Priority => 10;

    protected override IReadOnlyList<string> Signatures => ["PAYPHONE"];

    protected override IReadOnlyList<string> IncomeKeywords =>
    [
        "RECIBISTE", "PAGO RECIBIDO", "COBRO EXITOSO", "ACREDITACION",
    ];

    protected override IReadOnlyList<string> ExpenseKeywords =>
    [
        "PAGASTE", "PAGO REALIZADO", "COMPRA", "ENVIASTE", "TRANSFERENCIA ENVIADA",
    ];
}
