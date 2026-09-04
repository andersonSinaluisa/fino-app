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

    /// <summary>Throws when there is no authenticated user. Use in handlers that require one.</summary>
    Guid RequireUserId();
}
