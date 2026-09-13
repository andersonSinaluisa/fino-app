namespace Nexo.Domain.Providers;

/// <summary>
/// Canonical provider codes. Used as the join key between the catalogue, the
/// statement parsers, the email parsers and the mobile client's assets.
/// </summary>
public static class ProviderCodes
{
    public const string Pichincha = "PICHINCHA";
    public const string Guayaquil = "GUAYAQUIL";
    public const string Produbanco = "PRODUBANCO";
    public const string Pacifico = "PACIFICO";
    public const string Deuna = "DEUNA";
    public const string PayPhone = "PAYPHONE";
    public const string PeiGo = "PEIGO";

    /// <summary>
    /// Registro rápido de efectivo: el dinero en el bolsillo también es una
    /// cuenta real dentro del modelo financiero. No es una institución, así que
    /// no tiene parser de estados de cuenta ni conexión automática -- su único
    /// modo es <see cref="ConnectionMode.Manual"/>, y por eso nunca aparece en
    /// los flujos de "importar estado de cuenta" ni de "conectar correo".
    /// </summary>
    public const string Cash = "EFECTIVO";
    public const string Other = "OTRO";
}
