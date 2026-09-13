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

    /// <summary>
    /// Onboarding funcional (rediseño post-login): cuándo esta persona llegó al
    /// flujo por primera vez. Cinco marcas independientes en vez de un solo
    /// booleano -- "vio el tutorial", "ya tiene una cuenta" y "ya importó algo"
    /// son hechos distintos que pueden desalinearse (alguien puede saltar el
    /// tutorial y agregar una cuenta después desde Cuentas), y el cliente
    /// necesita saber exactamente dónde retomar si cerró la app a medias.
    /// Cada una se escribe una sola vez -- ver los métodos Record*/Complete*/Skip*
    /// más abajo, todos idempotentes.
    /// </summary>
    public DateTimeOffset? OnboardingStartedAt { get; private set; }

    /// <summary>Terminó el tutorial animado de "cómo descargar tu estado de cuenta" (sin saltarlo).</summary>
    public DateTimeOffset? OnboardingTutorialCompletedAt { get; private set; }

    /// <summary>Tocó "Ahora no" y confirmó "Configurar después" en el bottom sheet.</summary>
    public DateTimeOffset? OnboardingSkippedAt { get; private set; }

    /// <summary>
    /// Primera cuenta agregada -- se registra desde AccountService.CreateAsync,
    /// no desde una llamada explícita del onboarding, así que también queda
    /// marcada si la persona la agrega después desde Cuentas → Agregar cuenta
    /// en vez de durante el onboarding.
    /// </summary>
    public DateTimeOffset? FirstAccountAddedAt { get; private set; }

    /// <summary>
    /// Primera importación confirmada -- se registra desde ImportService.ConfirmAsync
    /// por la misma razón: el "primer import" es un hecho sobre la cuenta, no
    /// sobre qué pantalla lo disparó.
    /// </summary>
    public DateTimeOffset? FirstImportCompletedAt { get; private set; }

    /// <summary>
    /// Entregable 20 ("Hardening de seguridad"): consecutive wrong-password
    /// attempts since the last successful login or the last lockout. Reset to
    /// zero the moment a lockout starts, so the next window starts clean.
    /// </summary>
    public int FailedLoginAttempts { get; private set; }

    /// <summary>Entregable 20: set once <see cref="FailedLoginAttempts"/> reaches the configured limit.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

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
        FailedLoginAttempts = 0;
        LockedUntil = null;
        Stamp(now);
    }

    /// <summary>Entregable 20: true while a brute-force lockout is still in effect.</summary>
    public bool IsLockedOut(DateTimeOffset now) => LockedUntil is { } until && until > now;

    /// <summary>
    /// Entregable 20: records one more wrong-password attempt. Once the count
    /// reaches <paramref name="maxAttempts"/>, starts a lockout of
    /// <paramref name="lockoutDuration"/> and resets the counter so the next
    /// window starts clean. Returns true exactly on the attempt that triggers
    /// the lock, so the caller can audit it distinctly from an ordinary failure.
    /// </summary>
    public bool RegisterFailedLogin(int maxAttempts, TimeSpan lockoutDuration, DateTimeOffset now)
    {
        FailedLoginAttempts++;
        Stamp(now);

        if (FailedLoginAttempts < maxAttempts)
        {
            return false;
        }

        LockedUntil = now.Add(lockoutDuration);
        FailedLoginAttempts = 0;
        return true;
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

    /// <summary>
    /// Entregable 22 ("Privacidad completa"): the grace period only means
    /// something if there is a real way back. Every refresh token is revoked
    /// the moment deletion is requested, so the one door still open during the
    /// grace period is a fresh email+password login -- see
    /// AuthService.LoginAsync, which calls this the instant it verifies the
    /// password of a PendingDeletion account, before AccountDeletionWorker
    /// ever gets a chance to run.
    /// </summary>
    public void CancelDeletion(DateTimeOffset now)
    {
        Status = UserStatus.Active;
        DeletionRequestedAt = null;
        Stamp(now);
    }

    /// <summary>Reached the post-login onboarding flow. Idempotent: only the first arrival counts.</summary>
    public void StartOnboarding(DateTimeOffset now)
    {
        if (OnboardingStartedAt is null)
        {
            OnboardingStartedAt = now;
            Stamp(now);
        }
    }

    /// <summary>Finished the bank-tutorial animation (as opposed to skipping it). Idempotent.</summary>
    public void CompleteOnboardingTutorial(DateTimeOffset now)
    {
        if (OnboardingTutorialCompletedAt is null)
        {
            OnboardingTutorialCompletedAt = now;
            Stamp(now);
        }
    }

    /// <summary>Chose "Configurar después" instead of continuing. Idempotent -- changing your mind later and finishing the flow does not need to unset this; it is a historical fact, not a current state.</summary>
    public void SkipOnboarding(DateTimeOffset now)
    {
        if (OnboardingSkippedAt is null)
        {
            OnboardingSkippedAt = now;
            Stamp(now);
        }
    }

    /// <summary>Idempotent: called on every account creation, only ever sets this once.</summary>
    public void RecordFirstAccountAdded(DateTimeOffset now)
    {
        if (FirstAccountAddedAt is null)
        {
            FirstAccountAddedAt = now;
            Stamp(now);
        }
    }

    /// <summary>Idempotent: called on every confirmed import, only ever sets this once. This is PULSO's north star metric ("first successful import rate") made queryable straight off the user row.</summary>
    public void RecordFirstImportCompleted(DateTimeOffset now)
    {
        if (FirstImportCompletedAt is null)
        {
            FirstImportCompletedAt = now;
            Stamp(now);
        }
    }
}
