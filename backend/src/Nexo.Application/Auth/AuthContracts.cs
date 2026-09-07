namespace Nexo.Application.Auth;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record AuthenticatedUser(
    Guid Id,
    string Email,
    string DisplayName,
    string TimeZoneId,
    string Currency,
    string Locale);

/// <summary>
/// <c>AccountDeletionCancelled</c> (Entregable 22, "Privacidad completa") is
/// true exactly when this login undid a pending "eliminar mi cuenta" request
/// that was still inside its grace period -- lets the client tell the person
/// their account is no longer scheduled for deletion, instead of that fact
/// passing silently. Always false for register/refresh and for an ordinary
/// login.
/// </summary>
public sealed record AuthResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    AuthenticatedUser User,
    bool AccountDeletionCancelled = false);

/// <summary>Request metadata used for auditing. Never persisted in raw form.</summary>
public sealed record RequestContext(string? IpAddress, string? UserAgent, string? CorrelationId, string? DeviceLabel);

/// <summary>
/// Entregable 19 ("Sesiones y dispositivos"): one active RefreshToken row, which
/// -- since a token is single-use and rotates on every refresh -- is exactly one
/// device's currently live session. CreatedAt is this row's own issuance time,
/// so for a session that has been silently rotating in the background it reads
/// as "last active", not "logged in since"; that is the honest thing to show
/// without adding a second field just to track a chain's original start.
/// </summary>
public sealed record SessionDto(
    Guid Id,
    string? DeviceLabel,
    string? UserAgent,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    bool IsCurrent);
