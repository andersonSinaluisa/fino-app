using Nexo.Domain.Common;

namespace Nexo.Domain.Users;

/// <summary>Qué aceptó (o rechazó) la persona.</summary>
public enum ConsentKind
{
    /// <summary>Términos y condiciones, en una versión concreta.</summary>
    Terms = 0,

    /// <summary>Política de privacidad, en una versión concreta.</summary>
    Privacy = 1,

    /// <summary>Confirmó tener 18 años o más (requisito de los términos).</summary>
    AgeConfirmation = 2,

    /// <summary>Datos de uso anónimos para mejorar la app (opcional, revocable).</summary>
    Analytics = 3,
}

/// <summary>
/// LOPDP art. 8 y principio de responsabilidad demostrada (art. 10): cada
/// aceptación o revocación queda como una fila nueva -- nunca se edita una
/// anterior --, con la versión exacta del documento y el momento. La decisión
/// vigente de cada tipo es la fila más reciente. Se borra con la cuenta.
/// </summary>
public sealed class UserConsent : Entity, IUserOwned
{
    private UserConsent()
    {
    }

    public Guid UserId { get; private set; }

    public ConsentKind Kind { get; private set; }

    /// <summary>Versión del documento aceptado ("2026-10-02"); vacío para Analytics y AgeConfirmation.</summary>
    public string Version { get; private set; } = string.Empty;

    /// <summary>True = aceptó / dio el consentimiento; false = lo rechazó o revocó.</summary>
    public bool Granted { get; private set; }

    /// <summary>Dónde se obtuvo: "register", "reaccept", "settings".</summary>
    public string Source { get; private set; } = string.Empty;

    public static UserConsent Record(Guid userId, ConsentKind kind, string version, bool granted, string source, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("El consentimiento necesita un usuario.");
        }

        var consent = new UserConsent
        {
            UserId = userId,
            Kind = kind,
            Version = version.Trim(),
            Granted = granted,
            Source = source.Trim(),
        };
        consent.Stamp(now);
        return consent;
    }
}
