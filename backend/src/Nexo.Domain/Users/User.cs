using Nexo.Domain.Common;

namespace Nexo.Domain.Users;

public enum UserStatus
{
    Active = 0,
    Disabled = 1,
    PendingDeletion = 2,
}

public sealed class User : Entity
{
    private User()
    {
    }

    public string Email { get; private set; } = null!;

    /// <summary>Lower-cased email used for lookups and the unique index.</summary>
    public string NormalizedEmail { get; private set; } = null!;

    public string DisplayName { get; private set; } = null!;

    /// <summary>
    /// PHC-style string produced by the password hasher. Null when the user only
    /// signs in through an external identity provider or a magic link.
    /// </summary>
    public string? PasswordHash { get; private set; }

    public string TimeZoneId { get; private set; } = "America/Guayaquil";

    public string PreferredCurrency { get; private set; } = Currency.Usd;

    public string Locale { get; private set; } = "es-EC";

    public UserStatus Status { get; private set; } = UserStatus.Active;

    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset? DeletionRequestedAt { get; private set; }

    public static string NormalizeEmail(string email) =>
        DomainException.RequireText(email, nameof(email), 320).ToLowerInvariant();

    public static User Register(string email, string displayName, string? passwordHash, DateTimeOffset now)
    {
        var normalized = NormalizeEmail(email);
        if (!normalized.Contains('@', StringComparison.Ordinal) || normalized.StartsWith('@') || normalized.EndsWith('@'))
        {
            throw new DomainException("invalid_email", "The email address is not valid.");
        }

        var user = new User
        {
            Email = normalized,
            NormalizedEmail = normalized,
            DisplayName = DomainException.RequireText(displayName, nameof(displayName), 120),
            PasswordHash = passwordHash,
        };
        user.Stamp(now);
        return user;
    }

    public void SetPasswordHash(string passwordHash, DateTimeOffset now)
    {
        PasswordHash = DomainException.RequireText(passwordHash, nameof(passwordHash), 512);
        Stamp(now);
    }

    public void RecordLogin(DateTimeOffset now)
    {
        LastLoginAt = now;
        Stamp(now);
    }

    public void UpdateProfile(string? displayName, string? timeZoneId, string? locale, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            DisplayName = DomainException.RequireText(displayName, nameof(displayName), 120);
        }

        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            TimeZoneId = timeZoneId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(locale))
        {
            Locale = locale.Trim();
        }

        Stamp(now);
    }

    public void RequestDeletion(DateTimeOffset now)
    {
        Status = UserStatus.PendingDeletion;
        DeletionRequestedAt = now;
        Stamp(now);
    }
}
