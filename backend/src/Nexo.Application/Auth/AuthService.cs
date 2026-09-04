using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Domain.Audit;
using Nexo.Domain.Common;
using Nexo.Domain.Users;

namespace Nexo.Application.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Nexo:Auth";

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 30;

    public int MinimumPasswordLength { get; set; } = 10;

    /// <summary>Registration can be closed while the product is in private testing.</summary>
    public bool AllowSelfRegistration { get; set; } = true;
}

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, RequestContext context, CancellationToken cancellationToken);

    Task<AuthResult> LoginAsync(LoginRequest request, RequestContext context, CancellationToken cancellationToken);

    Task<AuthResult> RefreshAsync(RefreshRequest request, RequestContext context, CancellationToken cancellationToken);

    Task LogoutAsync(string refreshToken, RequestContext context, CancellationToken cancellationToken);

    Task LogoutAllAsync(Guid userId, RequestContext context, CancellationToken cancellationToken);
}

public sealed class AuthService(
    INexoDbContext db,
    IPasswordHasher passwordHasher,
    IAccessTokenService tokens,
    IIpHasher ipHasher,
    IClock clock,
    IOptions<AuthOptions> options,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly AuthOptions _options = options.Value;

    public async Task<AuthResult> RegisterAsync(
        RegisterRequest request,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        if (!_options.AllowSelfRegistration)
        {
            throw new ForbiddenException("El registro está cerrado por ahora.");
        }

        ValidatePassword(request.Password);

        var email = User.NormalizeEmail(request.Email);
        var exists = await db.Users.AnyAsync(u => u.NormalizedEmail == email, cancellationToken);
        if (exists)
        {
            // Deliberately the same wording as a bad password on login would give:
            // this endpoint must not become an account-existence oracle.
            throw new ConflictException("No se pudo crear la cuenta con ese correo.");
        }

        var now = clock.UtcNow;
        var user = User.Register(email, request.DisplayName, passwordHasher.Hash(request.Password), now);
        db.Users.Add(user);

        db.AuditLog.Add(AuditLogEntry.Record(
            user.Id,
            AuditActions.UserRegistered,
            nameof(User),
            now,
            user.Id.ToString(),
            context.CorrelationId,
            ipHasher.Hash(context.IpAddress),
            context.UserAgent));

        await db.SaveChangesAsync(cancellationToken);

        return await IssueAsync(user, context, cancellationToken);
    }

    public async Task<AuthResult> LoginAsync(
        LoginRequest request,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var email = User.NormalizeEmail(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == email, cancellationToken);
        var now = clock.UtcNow;

        if (user?.PasswordHash is null
            || !passwordHasher.Verify(request.Password, user.PasswordHash, out var needsRehash))
        {
            db.AuditLog.Add(AuditLogEntry.Record(
                user?.Id,
                AuditActions.UserLoginFailed,
                nameof(User),
                now,
                user?.Id.ToString(),
                context.CorrelationId,
                ipHasher.Hash(context.IpAddress),
                context.UserAgent,
                succeeded: false));
            await db.SaveChangesAsync(cancellationToken);

            throw new UnauthorizedException("Correo o contraseña incorrectos.");
        }

        if (user.Status != UserStatus.Active)
        {
            throw new ForbiddenException("Esta cuenta no está activa.");
        }

        if (needsRehash)
        {
            user.SetPasswordHash(passwordHasher.Hash(request.Password), now);
        }

        user.RecordLogin(now);

        db.AuditLog.Add(AuditLogEntry.Record(
            user.Id,
            AuditActions.UserLoggedIn,
            nameof(User),
            now,
            user.Id.ToString(),
            context.CorrelationId,
            ipHasher.Hash(context.IpAddress),
            context.UserAgent));

        await db.SaveChangesAsync(cancellationToken);

        return await IssueAsync(user, context, cancellationToken);
    }

    public async Task<AuthResult> RefreshAsync(
        RefreshRequest request,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var now = clock.UtcNow;

        var stored = await db.IgnoringUserFilter<RefreshToken>()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null)
        {
            throw new UnauthorizedException("La sesión expiró. Inicia sesión nuevamente.");
        }

        if (!stored.IsActive(now))
        {
            // Presenting an already-rotated token means the token leaked: kill the
            // whole family so the attacker's copy is useless too.
            await RevokeFamilyAsync(stored.UserId, "reuse_detected", now, cancellationToken);

            db.AuditLog.Add(AuditLogEntry.Record(
                stored.UserId,
                AuditActions.TokenReuseDetected,
                nameof(RefreshToken),
                now,
                stored.Id.ToString(),
                context.CorrelationId,
                ipHasher.Hash(context.IpAddress),
                context.UserAgent,
                succeeded: false));

            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Refresh token reuse detected for user {UserId}.", stored.UserId);

            throw new UnauthorizedException("La sesión expiró. Inicia sesión nuevamente.");
        }

        var user = await db.IgnoringUserFilter<User>()
            .FirstOrDefaultAsync(u => u.Id == stored.UserId, cancellationToken)
            ?? throw new UnauthorizedException();

        // A disabled account, or one pending deletion, must not be able to keep a
        // session alive by refreshing — the same gate login applies.
        if (user.Status != UserStatus.Active)
        {
            await RevokeFamilyAsync(user.Id, "account_not_active", now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            throw new ForbiddenException("Esta cuenta no está activa.");
        }

        var result = await IssueAsync(user, context, cancellationToken, rotating: stored);

        db.AuditLog.Add(AuditLogEntry.Record(
            user.Id,
            AuditActions.TokenRefreshed,
            nameof(RefreshToken),
            now,
            stored.Id.ToString(),
            context.CorrelationId,
            ipHasher.Hash(context.IpAddress),
            context.UserAgent));

        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task LogoutAsync(string refreshToken, RequestContext context, CancellationToken cancellationToken)
    {
        var hash = tokens.HashRefreshToken(refreshToken);
        var now = clock.UtcNow;

        var stored = await db.IgnoringUserFilter<RefreshToken>()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null)
        {
            return;
        }

        stored.Revoke("logout", now);

        db.AuditLog.Add(AuditLogEntry.Record(
            stored.UserId,
            AuditActions.UserLoggedOut,
            nameof(RefreshToken),
            now,
            stored.Id.ToString(),
            context.CorrelationId,
            ipHasher.Hash(context.IpAddress),
            context.UserAgent));

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task LogoutAllAsync(Guid userId, RequestContext context, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        await RevokeFamilyAsync(userId, "logout_all", now, cancellationToken);

        db.AuditLog.Add(AuditLogEntry.Record(
            userId,
            AuditActions.UserLoggedOut,
            nameof(RefreshToken),
            now,
            userId.ToString(),
            context.CorrelationId,
            ipHasher.Hash(context.IpAddress),
            context.UserAgent));

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<AuthResult> IssueAsync(
        User user,
        RequestContext context,
        CancellationToken cancellationToken,
        RefreshToken? rotating = null)
    {
        var now = clock.UtcNow;
        var access = tokens.Issue(user);
        var (refreshToken, refreshHash) = tokens.CreateRefreshToken();
        var refreshExpiry = now.AddDays(_options.RefreshTokenDays);

        var entity = RefreshToken.Issue(
            user.Id,
            refreshHash,
            refreshExpiry,
            now,
            context.DeviceLabel,
            ipHasher.Hash(context.IpAddress));

        db.RefreshTokens.Add(entity);
        rotating?.Revoke("rotated", now, entity.Id);

        await db.SaveChangesAsync(cancellationToken);

        return new AuthResult(
            access.Token,
            access.ExpiresAt,
            refreshToken,
            refreshExpiry,
            new AuthenticatedUser(
                user.Id,
                user.Email,
                user.DisplayName,
                user.TimeZoneId,
                user.PreferredCurrency,
                user.Locale));
    }

    private async Task RevokeFamilyAsync(Guid userId, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var active = await db.IgnoringUserFilter<RefreshToken>()
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in active)
        {
            token.Revoke(reason, now);
        }
    }

    private void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < _options.MinimumPasswordLength)
        {
            throw ValidationException.For(
                "password",
                $"La contraseña debe tener al menos {_options.MinimumPasswordLength} caracteres.");
        }

        if (password.Length > 256)
        {
            throw ValidationException.For("password", "La contraseña es demasiado larga.");
        }
    }
}
