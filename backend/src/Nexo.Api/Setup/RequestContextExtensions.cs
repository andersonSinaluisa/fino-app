using Nexo.Application.Auth;

namespace Nexo.Api.Setup;

public static class RequestContextExtensions
{
    /// <summary>
    /// Builds the audit context for a request. The raw IP never leaves this method:
    /// the auth service hands it straight to the hasher.
    /// </summary>
    public static RequestContext ToRequestContext(this HttpContext context) => new(
        context.Connection.RemoteIpAddress?.ToString(),
        Truncate(context.Request.Headers.UserAgent.ToString(), 200),
        context.TraceIdentifier,
        Truncate(context.Request.Headers["X-Device-Label"].FirstOrDefault(), 120));

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
}
