using System.Security.Claims;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;

namespace Nexo.Api.Setup;

/// <summary>
/// The one place a request turns into a user identity. Everything downstream — the
/// DbContext query filters included — reads the id from here, so there is a single
/// thing to audit when asking "can this request see another user's money?".
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public bool IsAuthenticated => UserId is not null;

    public Guid? SessionId
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirstValue("sid");
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public Guid RequireUserId() =>
        UserId ?? throw new UnauthorizedException("Necesitas iniciar sesión.");
}
