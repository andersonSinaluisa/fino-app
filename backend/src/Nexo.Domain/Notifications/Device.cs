using Nexo.Domain.Common;

namespace Nexo.Domain.Notifications;

public enum DevicePlatform
{
    Android = 0,
    Ios = 1,
    Web = 2,
}

/// <summary>
/// A mobile installation registered for push. Push tokens are not secrets in the
/// credential sense but they are personal data, so they are deleted with the user
/// and never written to logs.
/// </summary>
public sealed class Device : Entity, IUserOwned
{
    private Device()
    {
    }

    public Guid UserId { get; private set; }

    public string ExpoPushToken { get; private set; } = null!;

    public DevicePlatform Platform { get; private set; }

    public string? DeviceName { get; private set; }

    public string? AppVersion { get; private set; }

    public bool PushEnabled { get; private set; } = true;

    /// <summary>When false the push payload carries no amounts or merchant names.</summary>
    public bool ShowAmountsInPreview { get; private set; } = true;

    /// <summary>Categoría "Movimientos": gastos detectados e importaciones completadas.</summary>
    public bool NotifyOnMovements { get; private set; } = true;

    /// <summary>Categoría "Ingresos": dinero recibido detectado.</summary>
    public bool NotifyOnIncome { get; private set; } = true;

    /// <summary>Categoría "Insights": hallazgos nuevos del motor de insights.</summary>
    public bool NotifyOnInsights { get; private set; } = true;

    /// <summary>Categoría "Seguridad": correos sospechosos rechazados u otra alerta de seguridad.</summary>
    public bool NotifyOnSecurity { get; private set; } = true;

    /// <summary>Categoría "Recordatorios": resumen periódico y cuentas desactualizadas.</summary>
    public bool NotifyOnReminders { get; private set; } = true;

    /// <summary>PULSO FASE 3, categoría "Pulso": pulsos nuevos que pasan el filtro de PulseNotificationDecisionService.</summary>
    public bool NotifyOnPulses { get; private set; } = true;

    /// <summary>
    /// PULSO FASE 3 ("no molestar"): hora local (0-23, huso horario fijo de Ecuador --
    /// ver <see cref="NotificationDispatcher"/>) desde la que este dispositivo deja de
    /// recibir push. Null junto con <see cref="QuietHoursEndHour"/> significa que el
    /// horario silencioso está desactivado -- nunca uno sin el otro (ver <see cref="UpdatePreferences"/>).
    /// Nunca silencia la notificación en la app, solo el push: PushSentAt sigue
    /// registrándose y la persona la ve igual la próxima vez que abre Nexo.
    /// </summary>
    public int? QuietHoursStartHour { get; private set; }

    /// <summary>Hora local (0-23) en la que el horario silencioso termina. Puede ser menor que <see cref="QuietHoursStartHour"/> -- por ejemplo 22 a 7 cruza la medianoche.</summary>
    public int? QuietHoursEndHour { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public static Device Register(
        Guid userId,
        string expoPushToken,
        DevicePlatform platform,
        DateTimeOffset now,
        string? deviceName = null,
        string? appVersion = null)
    {
        var device = new Device
        {
            UserId = userId,
            ExpoPushToken = DomainException.RequireText(expoPushToken, nameof(expoPushToken), 255),
            Platform = platform,
            DeviceName = deviceName,
            AppVersion = appVersion,
            LastSeenAt = now,
        };
        device.Stamp(now);
        return device;
    }

    public void Touch(string? appVersion, DateTimeOffset now)
    {
        AppVersion = appVersion ?? AppVersion;
        LastSeenAt = now;
        Stamp(now);
    }

    public void UpdatePreferences(
        bool pushEnabled,
        bool showAmountsInPreview,
        bool notifyOnMovements,
        bool notifyOnIncome,
        bool notifyOnInsights,
        bool notifyOnSecurity,
        bool notifyOnReminders,
        bool notifyOnPulses,
        int? quietHoursStartHour,
        int? quietHoursEndHour,
        DateTimeOffset now)
    {
        // "No molestar" es las dos horas juntas o ninguna -- una sola no describe un
        // rango, y dejarla a medias sería silenciar el dispositivo por accidente.
        DomainException.Require(
            (quietHoursStartHour is null) == (quietHoursEndHour is null),
            "El horario sin avisos necesita una hora de inicio y una de fin, o ninguna.");
        DomainException.Require(
            quietHoursStartHour is null or >= 0 and <= 23,
            "La hora de inicio del horario sin avisos debe estar entre 0 y 23.");
        DomainException.Require(
            quietHoursEndHour is null or >= 0 and <= 23,
            "La hora de fin del horario sin avisos debe estar entre 0 y 23.");
        // Igual a igual no describe ningún rango -- ni "todo el día" ni "nunca" es
        // lo que alguien quiso decir al elegir la misma hora dos veces.
        DomainException.Require(
            quietHoursStartHour is null || quietHoursStartHour != quietHoursEndHour,
            "La hora de inicio y la de fin del horario sin avisos no pueden ser la misma.");

        PushEnabled = pushEnabled;
        ShowAmountsInPreview = showAmountsInPreview;
        NotifyOnMovements = notifyOnMovements;
        NotifyOnIncome = notifyOnIncome;
        NotifyOnInsights = notifyOnInsights;
        NotifyOnSecurity = notifyOnSecurity;
        NotifyOnReminders = notifyOnReminders;
        NotifyOnPulses = notifyOnPulses;
        QuietHoursStartHour = quietHoursStartHour;
        QuietHoursEndHour = quietHoursEndHour;
        Stamp(now);
    }
}
