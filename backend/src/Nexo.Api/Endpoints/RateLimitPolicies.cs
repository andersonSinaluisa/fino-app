namespace Nexo.Api.Endpoints;

public static class RateLimitPolicies
{
    /// <summary>Tight: login and refresh are the endpoints worth brute-forcing.</summary>
    public const string Authentication = "auth";

    /// <summary>Uploads are expensive to parse, so they get their own budget.</summary>
    public const string Uploads = "uploads";

    // Everything else falls under the global limiter configured in Program.cs.
    // A named policy that is never registered throws at endpoint-build time, so
    // constants only exist here for policies that are actually added.
}
