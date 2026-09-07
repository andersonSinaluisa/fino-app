using Nexo.Application.Abstractions;
using Nexo.Application.Analytics;

namespace Nexo.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/analytics")
            .WithTags("Analytics")
            .RequireAuthorization();

        group.MapGet("/dashboard", async (
                string? period,
                DateTimeOffset? from,
                DateTimeOffset? to,
                Guid? accountId,
                IAnalyticsService analytics,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            {
                var query = new AnalyticsQuery
                {
                    Period = ParsePeriod(period),
                    From = from,
                    To = to,
                    AccountId = accountId,
                };

                return Results.Ok(await analytics.GetDashboardAsync(currentUser.RequireUserId(), query, cancellationToken));
            })
            .WithSummary("Todo lo que necesita el dashboard de estadísticas: KPIs, series, categorías, comercios, saldo histórico.");

        return app;
    }

    private static AnalyticsPeriod ParsePeriod(string? period) => period?.Trim().ToLowerInvariant() switch
    {
        "last_month" => AnalyticsPeriod.LastMonth,
        "last_3_months" => AnalyticsPeriod.Last3Months,
        "last_6_months" => AnalyticsPeriod.Last6Months,
        "year" => AnalyticsPeriod.Year,
        "custom" => AnalyticsPeriod.Custom,
        _ => AnalyticsPeriod.Month,
    };
}
