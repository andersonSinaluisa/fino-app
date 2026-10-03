using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Application.Legal;
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

    /// <summary>
    /// Entregable 20 ("Hardening de seguridad"): consecutive wrong-password
    /// attempts allowed before the account is temporarily locked. This is on
    /// top of, not instead of, the per-IP rate limit on /auth/login -- the rate
    /// limit slows a distributed attacker; this stops one that simply waits out
    /// the rate-limit window against a single account.
    /// </summary>
    public int MaxFailedLoginAttempts { get; set; } = 5;

    /// <summary>How long an account stays locked out once <see cref="MaxFailedLoginAttempts"/> is reached.</summary>
    public int LockoutMinutes { get; set; } = 15;
}

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, RequestContext context, CancellationToken cancellationToken);

    Task<AuthResult> LoginAsync(LoginRequest request, RequestContext context, CancellationToken cancellationToken);

    Task<AuthResult> RefreshAsync(RefreshRequest request, RequestContext context, CancellationToken cancellationToken);

    Task LogoutAsync(string refreshToken, RequestContext context, CancellationToken cancellationToken);

    Task LogoutAllAsync(Guid userId, RequestContext context, CancellationToken cancellationToken);

    /// <summary>Every active session for the user, newest first, current one marked.</summary>
    Task<IReadOnlyList<SessionDto>> ListSessionsAsync(Guid userId, Guid? currentSessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes one specific session. Refuses to revoke the caller's own current
    /// session this way -- that is what /auth/logout is for, and doing it here
    /// would pull the rug out from under the same request that asked for it.
    /// </summary>
    Task RevokeSessionAsync(Guid userId, Guid sessionId, Guid? currentSessionId, RequestContext context, CancellationToken cancellationToken);
}

public sealed class AuthService(
    INexoDbContext db,
    IPasswordHasher passwordHasher,
    IAccessTokenService tokens,
    IIpHasher ipHasher,
    IClock clock,
    IOptions<AuthOptions> options,
    IOptions<LegalOptions> legalOptions,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly AuthOptions _options = options.Value;
    private readonly LegalOptions _legal = legalOptions.Value;

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

        // Antes de tocar la base: sin 18+ y aceptación explícita no hay cuenta.
        if (!request.AcceptedTerms || !request.ConfirmedAdult)
        {
            throw new ValidationException(
                "Para crear tu cuenta debes tener 18 años o más y aceptar los Términos y la Política de privacidad.");
        }

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
        LegalService.RecordRegistration(db, _legal, user.Id, request, now);

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

        // Entregable 20: an account mid-lockout is rejected before the password
        // is even checked, with the exact same message and shape as a wrong
        // password below -- a locked account must stay indistinguishable from
        // one that doesn't exist or was just given the wrong password.
        if (user is not null && user.IsLockedOut(now))
        {
            db.AuditLog.Add(AuditLogEntry.Record(
                user.Id,
                AuditActions.UserLoginBlocked,
                nameof(User),
                now,
                user.Id.ToString(),
                context.CorrelationId,
                ipHasher.Hash(context.IpAddress),
                context.UserAgent,
                succeeded: false));
            await db.SaveChangesAsync(cancellationToken);

            throw new UnauthorizedException("Correo o contraseña incorrectos.");
        }

        if (user?.PasswordHash is null
            || !passwordHasher.Verify(request.Password, user.PasswordHash, out var needsRehash))
        {
            // Only an existing account has anything to lock -- a nonexistent
            // email keeps behaving exactly as before.
            var justLocked = user is not null
                && user.RegisterFailedLogin(_options.MaxFailedLoginAttempts, TimeSpan.FromMinutes(_options.LockoutMinutes), now);

            db.AuditLog.Add(AuditLogEntry.Record(
                user?.Id,
                justLocked ? AuditActions.AccountLocked : AuditActions.UserLoginFailed,
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

        // Entregable 22 ("Privacidad completa"): RequestAccountDeletionAsync
        // revokes every refresh token immediately, so a fresh login is the
        // ONLY door left open during the grace period -- this is where "an
        // accidental request can still be undone" actually has to happen, or
        // it is not true. A Disabled account gets no such door: it stays
        // rejected exactly as before.
        var deletionCancelled = false;
        if (user.Status == UserStatus.PendingDeletion)
        {
            user.CancelDeletion(now);
            deletionCancelled = true;

            db.AuditLog.Add(AuditLogEntry.Record(
                user.Id,
                AuditActions.AccountDeletionCancelled,
                nameof(User),
                now,
                user.Id.ToString(),
                context.CorrelationId,
                ipHasher.Hash(context.IpAddress),
                context.UserAgent));
        }
        else if (user.Status != UserStatus.Active)
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

        var result = await IssueAsync(user, context, cancellationToken);
        return deletionCancelled ? result with { AccountDeletionCancelled = true } : result;
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

    public async Task<IReadOnlyList<SessionDto>> ListSessionsAsync(
        Guid userId,
        Guid? currentSessionId,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        return await db.IgnoringUserFilter<RefreshToken>()
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new SessionDto(t.Id, t.DeviceLabel, t.UserAgent, t.CreatedAt, t.ExpiresAt, t.Id == currentSessionId))
            .ToListAsync(cancellationToken);
    }

    public async Task RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        Guid? currentSessionId,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        if (sessionId == currentSessionId)
        {
            throw new ConflictException("Para cerrar la sesión de este dispositivo, usa \"Cerrar sesión\".");
        }

        var now = clock.UtcNow;
        var session = await db.IgnoringUserFilter<RefreshToken>()
            .FirstOrDefaultAsync(t => t.Id == sessionId && t.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Session", sessionId);

        if (!session.IsActive(now))
        {
            // Already gone (expired or previously revoked): nothing left to do,
            // and the person asked for exactly this outcome either way.
            return;
        }

        session.Revoke("revoked_by_user", now);

        db.AuditLog.Add(AuditLogEntry.Record(
            userId,
            AuditActions.UserLoggedOut,
            nameof(RefreshToken),
            now,
            session.Id.ToString(),
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
        var (refreshToken, refreshHash) = tokens.CreateRefreshToken();
        var refreshExpiry = now.AddDays(_options.RefreshTokenDays);

        var entity = RefreshToken.Issue(
            user.Id,
            refreshHash,
            refreshExpiry,
            now,
            context.DeviceLabel,
            ipHasher.Hash(context.IpAddress),
            context.UserAgent);

        db.RefreshTokens.Add(entity);
        rotating?.Revoke("rotated", now, entity.Id);

        // The access token's "sid" claim is this row's id, so a request can find
        // its own session without ever resending the refresh token itself.
        var access = tokens.Issue(user, entity.Id);

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
                user.Locale,
                new OnboardingStatusDto(
                    user.OnboardingStartedAt,
                    user.OnboardingTutorialCompletedAt,
                    user.OnboardingSkippedAt,
                    user.FirstAccountAddedAt,
                    user.FirstImportCompletedAt)));
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
