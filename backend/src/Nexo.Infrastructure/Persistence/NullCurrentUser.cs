using Nexo.Application.Abstractions;
using Nexo.Application.Common;

namespace Nexo.Infrastructure.Persistence;

/// <summary>
/// Used by hosts with no request principal: background workers, the seeder and
/// design-time tooling. Query filters become inert, which is safe because those
/// paths always scope by user explicitly.
/// </summary>
public sealed class NullCurrentUser : ICurrentUser
{
    public Guid? UserId => null;

    public bool IsAuthenticated => false;

    public Guid? SessionId => null;

    public Guid RequireUserId() =>
        throw new UnauthorizedException("This operation requires an authenticated user.");
}
