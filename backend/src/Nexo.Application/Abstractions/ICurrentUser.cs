namespace Nexo.Application.Abstractions;

/// <summary>
/// The authenticated principal for the current request. Infrastructure resolves it
/// from the JWT; the DbContext uses it to apply the per-user query filter, so this
/// is the single point where tenant isolation is decided.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    bool IsAuthenticated { get; }

    /// <summary>
    /// Entregable 19: the RefreshToken row behind the access token making this
    /// request, from the "sid" claim -- lets the session-list endpoint mark
    /// "this device" without the client resending its refresh token. Null for
    /// a token minted before this claim existed, or with no session backing it.
    /// </summary>
    Guid? SessionId { get; }

    /// <summary>Throws when there is no authenticated user. Use in handlers that require one.</summary>
    Guid RequireUserId();
}
