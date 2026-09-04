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

public sealed record AuthResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    AuthenticatedUser User);

/// <summary>Request metadata used for auditing. Never persisted in raw form.</summary>
public sealed record RequestContext(string? IpAddress, string? UserAgent, string? CorrelationId, string? DeviceLabel);
