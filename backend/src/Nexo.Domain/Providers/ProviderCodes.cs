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
    public const string Other = "OTRO";
}
