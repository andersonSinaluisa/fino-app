namespace Nexo.Application.Legal;

/// <summary>
/// Datos del responsable y versiones de los documentos legales. Todo es
/// configurable (appsettings / variables de entorno <c>Nexo__Legal__*</c>)
/// para no publicar datos personales o de empresa en el código.
///
/// Cambiar <see cref="TermsVersion"/> o <see cref="PrivacyVersion"/> obliga a
/// cada persona a aceptar de nuevo al abrir la app (ver LegalService).
/// </summary>
public sealed class LegalOptions
{
    public const string SectionName = "Nexo:Legal";

    /// <summary>Nombre de la persona natural o razón social responsable del tratamiento.</summary>
    public string ControllerName { get; set; } = string.Empty;

    /// <summary>Cédula o RUC del responsable.</summary>
    public string ControllerId { get; set; } = string.Empty;

    /// <summary>Dirección del responsable (ciudad, Ecuador).</summary>
    public string ControllerAddress { get; set; } = string.Empty;

    /// <summary>Correo para privacidad, soporte y reclamos.</summary>
    public string ContactEmail { get; set; } = "info@brix-dev.com";

    /// <summary>Delegado de protección de datos, si se designó. Vacío = no designado.</summary>
    public string DataProtectionOfficer { get; set; } = string.Empty;

    /// <summary>Proveedor y país de los servidores, p. ej. "Hetzner (Alemania)".</summary>
    public string HostingProvider { get; set; } = string.Empty;

    public string TermsVersion { get; set; } = "2026-10-02";

    public string PrivacyVersion { get; set; } = "2026-10-02";

    /// <summary>Días antes de borrar definitivamente una cuenta cuya eliminación se pidió.</summary>
    public int AccountDeletionGraceDays { get; set; } = 7;

    /// <summary>Faltan datos del responsable: el documento se publica con avisos "(pendiente)".</summary>
    public bool IsIncomplete =>
        string.IsNullOrWhiteSpace(ControllerName)
        || string.IsNullOrWhiteSpace(ControllerId)
        || string.IsNullOrWhiteSpace(ControllerAddress)
        || string.IsNullOrWhiteSpace(HostingProvider);
}
