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
    DateTimeOffset PeriodEnd,
    // Entregable 16 ("Insights v1"): when this observation stops being relevant.
    // Both readers already filter it out server-side once past this instant --
    // it rides along mainly so a client that cached a response can tell too.
    DateTimeOffset ValidUntil);
