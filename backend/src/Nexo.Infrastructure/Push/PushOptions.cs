namespace Nexo.Infrastructure.Push;

public sealed class PushOptions
{
    public const string SectionName = "Nexo:Push";

    public bool Enabled { get; set; }

    /// <summary>
    /// Token de acceso de Expo (expo.dev → Access tokens). Obligatorio si el
    /// proyecto tiene activada "Enhanced Security for Push Notifications":
    /// sin él Expo rechaza los envíos. Secreto: solo en .env.production.
    /// </summary>
    public string ExpoAccessToken { get; set; } = string.Empty;
}
