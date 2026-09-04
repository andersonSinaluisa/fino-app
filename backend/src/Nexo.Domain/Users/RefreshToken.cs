using Nexo.Domain.Common;

namespace Nexo.Domain.Users;

/// <summary>
/// Refresh tokens are stored hashed (never in clear text) and are single-use:
/// redeeming one issues a replacement and marks the old one as rotated. Reuse of
/// an already-rotated token revokes the whole chain, which is the standard
/// detection mechanism for a stolen refresh token.
/// </summary>
public sealed class RefreshToken : Entity, IUserOwned
{
    private RefreshToken()
    {
    }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokedReason { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public string? DeviceLabel { get; private set; }

    public string? CreatedFromIpHash { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public static RefreshToken Issue(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        string? deviceLabel = null,
        string? createdFromIpHash = null)
    {
        var token = new RefreshToken
        {
            UserId = userId,
            TokenHash = DomainException.RequireText(tokenHash, nameof(tokenHash)),
            ExpiresAt = expiresAt,
            DeviceLabel = deviceLabel,
            CreatedFromIpHash = createdFromIpHash,
        };
        token.Stamp(now);
        return token;
    }

    public void Revoke(string reason, DateTimeOffset now, Guid? replacedBy = null)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
        ReplacedByTokenId = replacedBy;
        Stamp(now);
    }
}
