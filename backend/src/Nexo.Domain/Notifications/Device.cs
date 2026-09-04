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

    public bool NotifyOnNewTransaction { get; private set; } = true;

    public bool NotifyOnImportFinished { get; private set; } = true;

    public bool NotifyOnWeeklySummary { get; private set; } = true;

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
        bool notifyOnNewTransaction,
        bool notifyOnImportFinished,
        bool notifyOnWeeklySummary,
        DateTimeOffset now)
    {
        PushEnabled = pushEnabled;
        ShowAmountsInPreview = showAmountsInPreview;
        NotifyOnNewTransaction = notifyOnNewTransaction;
        NotifyOnImportFinished = notifyOnImportFinished;
        NotifyOnWeeklySummary = notifyOnWeeklySummary;
        Stamp(now);
    }
}
