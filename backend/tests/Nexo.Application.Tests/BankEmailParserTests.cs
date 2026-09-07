using Microsoft.Extensions.Logging.Abstractions;
using Nexo.Application.EmailIngestion;
using Nexo.Application.EmailIngestion.Parsers;
using Nexo.Domain.EmailIngestion;
using Nexo.Domain.Providers;
using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Application.Tests;

/// <summary>
/// Fixtures are entirely fictional: invented amounts, invented merchants, invented
/// card digits. No real customer notification is stored in this repository.
/// </summary>
public class BankEmailParserTests
{
    private static readonly DateTimeOffset Received = new(2026, 3, 4, 23, 20, 0, TimeSpan.Zero);

    private static readonly IBankEmailParser[] AllParsers =
    [
        new PichinchaEmailParser(),
        new GuayaquilEmailParser(),
        new ProdubancoEmailParser(),
        new PacificoEmailParser(),
        new DeunaEmailParser(),
        new PayPhoneEmailParser(),
    ];

    private static BankEmailParserResolver Resolver() =>
        new(AllParsers, NullLogger<BankEmailParserResolver>.Instance);

    private static EmailMessage Message(
        string subject,
        string body,
        string from = "notificaciones@pichincha.com",
        bool authenticated = true) =>
        new(
            Guid.CreateVersion7().ToString("N"),
            from,
            "Banco Pichincha",
            subject,
            body,
            Received,
            authenticated);

    [Fact]
    public async Task A_purchase_notification_becomes_an_expense()
    {
        var message = Message(
            "Banco Pichincha: compra realizada",
            """
            Estimado cliente,
            Se realizó una compra por USD 48,20 en SUPERMAXI ALBORADA
            con su tarjeta terminada en 4821 el 04/03/2026 a las 18:12.
            Referencia: TRX-99881
            """);

        var parser = Resolver().Resolve(message, ProviderCodes.Pichincha);
        Assert.NotNull(parser);

        var parsed = await parser.ParseAsync(message, CancellationToken.None);

        Assert.NotNull(parsed);
        Assert.Equal(TransactionDirection.Expense, parsed.Direction);
        Assert.Equal(48.20m, parsed.Amount);
        Assert.Equal("4821", parsed.AccountMask);
        Assert.Equal("TRX-99881", parsed.ExternalReference);
        Assert.Equal(SourceConfidence.Medium, parsed.Confidence);
        Assert.Equal("Supermaxi Alborada", parsed.Merchant);
    }

    [Fact]
    public async Task The_merchant_is_the_shop_name_not_the_rest_of_the_sentence()
    {
        // Single-line body: a greedy pattern would capture everything up to the end
        // and make the description "Tienda Terminada", which would then break
        // deduplication against the statement row.
        var message = Message(
            "Banco Pichincha: compra realizada",
            "Se realizó una compra por USD 10,00 en TIENDA X con su tarjeta terminada en 4821 el 04/03/2026.");

        var parser = Resolver().Resolve(message, ProviderCodes.Pichincha)!;
        var parsed = await parser.ParseAsync(message, CancellationToken.None);

        Assert.NotNull(parsed);
        Assert.Equal("Tienda", parsed.Merchant);
    }

    [Fact]
    public async Task A_sentence_without_a_shop_name_does_not_invent_a_merchant()
    {
        var message = Message(
            "Banco Pichincha: transferencia recibida",
            "Registramos una transferencia recibida por USD 350,00 en su cuenta terminada en 4821 el 05/03/2026.");

        var parser = Resolver().Resolve(message, ProviderCodes.Pichincha)!;
        var parsed = await parser.ParseAsync(message, CancellationToken.None);

        Assert.NotNull(parsed);
        Assert.Null(parsed.Merchant);
        Assert.Equal("Banco Pichincha: transferencia recibida", parsed.Description);
    }

    [Fact]
    public async Task An_incoming_transfer_becomes_income()
    {
        var message = Message(
            "Banco Pichincha: transferencia recibida",
            """
            Estimado cliente,
            Registramos una transferencia recibida por USD 350,00 en su cuenta
            terminada en 4821 el 05/03/2026.
            """);

        var parser = Resolver().Resolve(message, ProviderCodes.Pichincha)!;
        var parsed = await parser.ParseAsync(message, CancellationToken.None);

        Assert.NotNull(parsed);
        Assert.Equal(TransactionDirection.Income, parsed.Direction);
        Assert.Equal(350.00m, parsed.Amount);
    }

    [Fact]
    public async Task A_wallet_notification_uses_the_wallet_wording()
    {
        var message = Message(
            "DEUNA: enviaste dinero",
            "Enviaste $25,00 a Juan Pérez el 04/03/2026. Comprobante: DEU-77120",
            from: "no-reply@deuna.app");

        var parser = Resolver().Resolve(message, ProviderCodes.Deuna);
        Assert.NotNull(parser);
        Assert.Equal("DEUNA_EMAIL_V1", parser.ParserCode);

        var parsed = await parser.ParseAsync(message, CancellationToken.None);

        Assert.NotNull(parsed);
        Assert.Equal(TransactionDirection.Expense, parsed.Direction);
        Assert.Equal(25.00m, parsed.Amount);
    }

    [Fact]
    public void A_marketing_email_from_the_bank_is_not_a_movement()
    {
        var message = Message(
            "Tu estado de cuenta disponible",
            "Ya está disponible tu estado de cuenta del mes. Ingresa a la banca web para revisarlo.");

        Assert.Null(Resolver().Resolve(message, ProviderCodes.Pichincha));
    }

    [Fact]
    public void A_security_alert_is_not_a_movement()
    {
        var message = Message(
            "Cambio de clave exitoso",
            "Tu cambio de clave se realizó con éxito el 04/03/2026.");

        Assert.Null(Resolver().Resolve(message, ProviderCodes.Pichincha));
    }

    [Fact]
    public void A_parser_never_claims_a_message_attributed_to_another_provider()
    {
        var message = Message(
            "Banco Pichincha: compra realizada",
            "Se realizó una compra por USD 10,00 en TIENDA X con su tarjeta terminada en 4821.");

        Assert.Null(Resolver().Resolve(message, ProviderCodes.Guayaquil));
    }

    [Fact]
    public async Task Critical_case_9_a_forged_bank_email_is_rejected_by_sender_validation()
    {
        // The parser itself would happily read this message: the display name says
        // "Banco Pichincha" and the wording is right. What stops it is the sender
        // allow-list plus mail authentication, one stage earlier.
        var forged = new EmailMessage(
            "forged-1",
            "cobros@banco-pichincha-seguro.example",
            "Banco Pichincha",
            "Banco Pichincha: compra realizada",
            "Se realizó una compra por USD 999,00 en TIENDA FALSA con su tarjeta terminada en 4821.",
            Received,
            PassedAuthentication: false);

        var trusted = TrustedSender.Domain(ProviderCodes.Pichincha, "pichincha.com", Received);

        Assert.False(trusted.Accepts(forged.EnvelopeFrom, forged.PassedAuthentication));
        Assert.False(trusted.Accepts(forged.EnvelopeFrom, authenticated: true));

        // And a genuine, authenticated sender is accepted.
        Assert.True(trusted.Accepts("notificaciones@pichincha.com", authenticated: true));

        // Even a real domain is refused when the message failed SPF/DKIM/DMARC.
        Assert.False(trusted.Accepts("notificaciones@pichincha.com", authenticated: false));

        await Task.CompletedTask;
    }

    [Fact]
    public void A_subdomain_of_a_trusted_domain_is_accepted_but_a_lookalike_is_not()
    {
        var trusted = TrustedSender.Domain(ProviderCodes.Pichincha, "pichincha.com", Received);

        Assert.True(trusted.Accepts("alertas@mail.pichincha.com", authenticated: true));
        Assert.False(trusted.Accepts("alertas@pichincha.com.example", authenticated: true));
        Assert.False(trusted.Accepts("alertas@notpichincha.com", authenticated: true));
    }

    [Fact]
    public async Task The_notification_time_is_interpreted_in_Ecuador_time()
    {
        var message = Message(
            "Banco Pichincha: compra realizada",
            "Se realizó una compra por USD 12,00 en TIENDA el 31/03/2026 a las 23:30 con su tarjeta terminada en 4821.");

        var parser = Resolver().Resolve(message, ProviderCodes.Pichincha)!;
        var parsed = await parser.ParseAsync(message, CancellationToken.None);

        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(2026, 4, 1, 4, 30, 0), parsed.OccurredAt.UtcDateTime);
    }

    /// <summary>
    /// Entregable 26 ("Parsers de correo"): before this, "6:12 PM" was read as a
    /// plain 24-hour "06:12" -- the meridiem was matched by nothing and silently
    /// dropped, so a real afternoon purchase landed twelve hours off.
    /// </summary>
    [Fact]
    public async Task A_12_hour_clock_time_with_PM_is_read_as_the_afternoon_hour()
    {
        var message = Message(
            "Banco Pichincha: compra realizada",
            "Se realizó una compra por USD 12,00 en TIENDA el 31/03/2026 a las 6:12 PM con su tarjeta terminada en 4821.");

        var parser = Resolver().Resolve(message, ProviderCodes.Pichincha)!;
        var parsed = await parser.ParseAsync(message, CancellationToken.None);

        Assert.NotNull(parsed);
        // 18:12 Ecuador time (UTC-5, no DST) on 31/03/2026 is 23:12 UTC the same day.
        Assert.Equal(new DateTime(2026, 3, 31, 23, 12, 0), parsed.OccurredAt.UtcDateTime);
    }

    /// <summary>
    /// Entregable 26: a completely ordinary real notification shape -- the balance
    /// mentioned first must never win over the actual movement amount that follows.
    /// </summary>
    [Fact]
    public async Task The_purchase_amount_wins_over_an_available_balance_mentioned_earlier()
    {
        var message = Message(
            "Banco Pichincha: compra realizada",
            "Su saldo disponible es de USD 1.204,33. Se realizó una compra por USD 48,20 en SUPERMAXI ALBORADA "
            + "con su tarjeta terminada en 4821 el 04/03/2026.");

        var parser = Resolver().Resolve(message, ProviderCodes.Pichincha)!;
        var parsed = await parser.ParseAsync(message, CancellationToken.None);

        Assert.NotNull(parsed);
        Assert.Equal(48.20m, parsed.Amount);
    }

    /// <summary>
    /// Entregable 26: relays are not guaranteed to hand over entity-decoded plain
    /// text. An undecoded "&amp;amp;" used to poison the merchant capture with a
    /// literal "Amp" token once normalized.
    /// </summary>
    [Fact]
    public async Task An_undecoded_html_entity_does_not_poison_the_merchant_name()
    {
        var message = Message(
            "Banco Pichincha: compra realizada",
            "Se realizó una compra por USD 48,20 en SUPERMAXI&amp;ALBORADA con su tarjeta terminada en 4821 "
            + "el 04/03/2026.");

        var parser = Resolver().Resolve(message, ProviderCodes.Pichincha)!;
        var parsed = await parser.ParseAsync(message, CancellationToken.None);

        Assert.NotNull(parsed);
        Assert.Equal("Supermaxi Alborada", parsed.Merchant);
    }

    /// <summary>
    /// Entregable 26: a payment-due reminder says "pago" next to a dollar figure
    /// without describing anything that already happened -- exactly what the bare
    /// "PAGO" expense keyword would otherwise misread as a completed expense.
    /// </summary>
    [Fact]
    public void A_payment_due_reminder_is_not_a_movement()
    {
        var message = Message(
            "Recordatorio de pago",
            "Tu fecha de pago es el 15/03/2026 y el monto a pagar es de USD 120,00. Evita intereses pagando a tiempo.");

        Assert.Null(Resolver().Resolve(message, ProviderCodes.Pichincha));
    }

    /// <summary>
    /// Entregable 26: the previous test's fix must not throw out a genuine,
    /// already-completed payment just because it also contains the word "pago".
    /// </summary>
    [Fact]
    public async Task A_completed_payment_notification_is_still_an_expense()
    {
        var message = Message(
            "Banco Pichincha: pago realizado",
            "Se realizó un pago de USD 45,00 a favor de EMPRESA XYZ el 10/03/2026.");

        var parser = Resolver().Resolve(message, ProviderCodes.Pichincha)!;
        var parsed = await parser.ParseAsync(message, CancellationToken.None);

        Assert.NotNull(parsed);
        Assert.Equal(TransactionDirection.Expense, parsed.Direction);
        Assert.Equal(45.00m, parsed.Amount);
    }
}
