using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexo.Application.Abstractions;
using Nexo.Application.Auth;
using Nexo.Application.Common;
using Nexo.Domain.Audit;
using Nexo.Domain.Common;
using Nexo.Domain.Users;

namespace Nexo.Application.Legal;

public enum LegalDocumentKind
{
    Terms,
    Privacy,
}

/// <summary><c>IsIncomplete</c>: faltan datos del responsable en la configuración (el texto lleva "(pendiente)").</summary>
public sealed record LegalDocumentDto(
    string Kind,
    string Title,
    string Version,
    string Markdown,
    bool IsIncomplete);

/// <summary>
/// Lo que la app necesita al abrir: versiones vigentes, cuáles aceptó la
/// persona y si debe aceptar de nuevo; y su decisión sobre datos de uso
/// (null = nunca se le preguntó).
/// </summary>
public sealed record LegalStatusDto(
    string TermsVersion,
    string PrivacyVersion,
    string? AcceptedTermsVersion,
    string? AcceptedPrivacyVersion,
    bool NeedsAcceptance,
    bool? AnalyticsConsent,
    string ContactEmail);

/// <summary>Volver a aceptar tras un cambio de versión. <c>ConfirmedAdult</c> es obligatorio: las cuentas anteriores a este flujo nunca lo confirmaron.</summary>
public sealed record AcceptLegalRequest(string TermsVersion, string PrivacyVersion, bool ConfirmedAdult = false, bool? AnalyticsConsent = null);

public sealed record AnalyticsConsentRequest(bool Granted);

public interface ILegalService
{
    LegalDocumentDto GetDocument(LegalDocumentKind kind);

    Task<LegalStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken);

    Task<LegalStatusDto> AcceptAsync(Guid userId, AcceptLegalRequest request, RequestContext context, CancellationToken cancellationToken);

    Task<LegalStatusDto> SetAnalyticsConsentAsync(Guid userId, bool granted, RequestContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Documentos legales y consentimientos (LOPDP arts. 8, 10 y 12). Cada
/// decisión se guarda como una fila nueva en <c>user_consents</c>; nunca se
/// reescribe una anterior.
/// </summary>
public sealed class LegalService(
    INexoDbContext db,
    IIpHasher ipHasher,
    IClock clock,
    IOptions<LegalOptions> options) : ILegalService
{
    private readonly LegalOptions _options = options.Value;

    public const string SourceRegister = "register";
    public const string SourceReaccept = "reaccept";
    public const string SourceSettings = "settings";

    public LegalDocumentDto GetDocument(LegalDocumentKind kind) => Render(_options, kind);

    internal static LegalDocumentDto Render(LegalOptions options, LegalDocumentKind kind)
    {
        var (title, template, version) = kind == LegalDocumentKind.Terms
            ? (LegalTexts.TermsTitle, LegalTexts.Terms, options.TermsVersion)
            : (LegalTexts.PrivacyTitle, LegalTexts.Privacy, options.PrivacyVersion);

        static string Or(string value) => string.IsNullOrWhiteSpace(value) ? "(pendiente de completar)" : value.Trim();

        var markdown = template
            .Replace("{{RESPONSABLE}}", Or(options.ControllerName), StringComparison.Ordinal)
            .Replace("{{IDENTIFICACION}}", Or(options.ControllerId), StringComparison.Ordinal)
            .Replace("{{DIRECCION}}", Or(options.ControllerAddress), StringComparison.Ordinal)
            .Replace("{{CORREO}}", Or(options.ContactEmail), StringComparison.Ordinal)
            .Replace("{{HOSTING}}", Or(options.HostingProvider), StringComparison.Ordinal)
            .Replace(
                "{{DELEGADO}}",
                string.IsNullOrWhiteSpace(options.DataProtectionOfficer)
                    ? $"no designado por ahora; las consultas se atienden en {Or(options.ContactEmail)}"
                    : options.DataProtectionOfficer.Trim(),
                StringComparison.Ordinal)
            .Replace("{{VERSION}}", version, StringComparison.Ordinal)
            .Replace("{{GRACIA}}", Math.Max(1, options.AccountDeletionGraceDays).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Trim();

        return new LegalDocumentDto(
            kind == LegalDocumentKind.Terms ? "terminos" : "privacidad",
            title,
            version,
            markdown,
            options.IsIncomplete);
    }

    public async Task<LegalStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var latest = await LatestAsync(userId, cancellationToken);
        return BuildStatus(latest);
    }

    public async Task<LegalStatusDto> AcceptAsync(
        Guid userId,
        AcceptLegalRequest request,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        EnsureCurrentVersions(_options, request.TermsVersion, request.PrivacyVersion);
        if (!request.ConfirmedAdult)
        {
            throw new ValidationException("Para usar Fino debes confirmar que tienes 18 años o más.");
        }

        var now = clock.UtcNow;
        db.UserConsents.Add(UserConsent.Record(userId, ConsentKind.AgeConfirmation, string.Empty, true, SourceReaccept, now));
        db.UserConsents.Add(UserConsent.Record(userId, ConsentKind.Terms, _options.TermsVersion, true, SourceReaccept, now));
        db.UserConsents.Add(UserConsent.Record(userId, ConsentKind.Privacy, _options.PrivacyVersion, true, SourceReaccept, now));
        if (request.AnalyticsConsent is { } analytics)
        {
            db.UserConsents.Add(UserConsent.Record(userId, ConsentKind.Analytics, string.Empty, analytics, SourceReaccept, now));
        }

        Audit(userId, AuditActions.LegalDocumentsAccepted, context, now);
        await db.SaveChangesAsync(cancellationToken);
        return await GetStatusAsync(userId, cancellationToken);
    }

    public async Task<LegalStatusDto> SetAnalyticsConsentAsync(
        Guid userId,
        bool granted,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        db.UserConsents.Add(UserConsent.Record(userId, ConsentKind.Analytics, string.Empty, granted, SourceSettings, now));
        Audit(userId, AuditActions.ConsentChanged, context, now);
        await db.SaveChangesAsync(cancellationToken);
        return await GetStatusAsync(userId, cancellationToken);
    }

    /// <summary>
    /// Usado por el registro: valida que la persona confirmó 18+ y aceptó las
    /// versiones vigentes, y agrega las filas de consentimiento al mismo
    /// SaveChanges que crea el usuario (todo o nada).
    /// </summary>
    internal static void RecordRegistration(
        INexoDbContext db,
        LegalOptions options,
        Guid userId,
        RegisterRequest request,
        DateTimeOffset now)
    {
        if (!request.AcceptedTerms || !request.ConfirmedAdult)
        {
            throw new ValidationException(
                "Para crear tu cuenta debes tener 18 años o más y aceptar los Términos y la Política de privacidad.");
        }

        EnsureCurrentVersions(options, request.TermsVersion ?? options.TermsVersion, request.PrivacyVersion ?? options.PrivacyVersion);

        db.UserConsents.Add(UserConsent.Record(userId, ConsentKind.AgeConfirmation, string.Empty, true, SourceRegister, now));
        db.UserConsents.Add(UserConsent.Record(userId, ConsentKind.Terms, options.TermsVersion, true, SourceRegister, now));
        db.UserConsents.Add(UserConsent.Record(userId, ConsentKind.Privacy, options.PrivacyVersion, true, SourceRegister, now));
        db.UserConsents.Add(UserConsent.Record(userId, ConsentKind.Analytics, string.Empty, request.AnalyticsConsent ?? false, SourceRegister, now));
    }

    private static void EnsureCurrentVersions(LegalOptions options, string termsVersion, string privacyVersion)
    {
        if (!string.Equals(termsVersion?.Trim(), options.TermsVersion, StringComparison.Ordinal)
            || !string.Equals(privacyVersion?.Trim(), options.PrivacyVersion, StringComparison.Ordinal))
        {
            throw new ValidationException("Los Términos o la Política de privacidad cambiaron. Revísalos de nuevo antes de aceptar.");
        }
    }

    private async Task<Dictionary<ConsentKind, UserConsent>> LatestAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await db.UserConsents
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(c => c.Kind)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).First());
    }

    private LegalStatusDto BuildStatus(Dictionary<ConsentKind, UserConsent> latest)
    {
        var terms = latest.GetValueOrDefault(ConsentKind.Terms);
        var privacy = latest.GetValueOrDefault(ConsentKind.Privacy);
        var analytics = latest.GetValueOrDefault(ConsentKind.Analytics);

        var acceptedTerms = terms is { Granted: true } ? terms.Version : null;
        var acceptedPrivacy = privacy is { Granted: true } ? privacy.Version : null;

        return new LegalStatusDto(
            _options.TermsVersion,
            _options.PrivacyVersion,
            acceptedTerms,
            acceptedPrivacy,
            NeedsAcceptance: acceptedTerms != _options.TermsVersion || acceptedPrivacy != _options.PrivacyVersion,
            AnalyticsConsent: analytics?.Granted,
            ContactEmail: _options.ContactEmail);
    }

    private void Audit(Guid userId, string action, RequestContext context, DateTimeOffset now) =>
        db.AuditLog.Add(AuditLogEntry.Record(
            userId,
            action,
            nameof(UserConsent),
            now,
            userId.ToString(),
            context.CorrelationId,
            ipHasher.Hash(context.IpAddress),
            context.UserAgent));
}
