namespace Nexo.Application.Insights;

public sealed record InsightDto(
    string Code,
    string Title,
    string Body,
    decimal? Value,
    decimal? ComparisonValue,
    decimal? PercentChange,
    string Severity,
    string? ReferenceId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd);
