namespace Nexo.Application.Pulses;

public sealed record PulseDto(
    Guid Id,
    string Type,
    string Severity,
    string Title,
    string Body,
    string Explanation,
    decimal? Value,
    decimal? ComparisonValue,
    decimal? PercentChange,
    string? ReferenceId,
    decimal RelevanceScore,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt,
    bool? FeedbackHelpful);

/// <summary>PULSO FASE 4: body of POST /pulses/{id}/feedback -- 👍/👎 on the detail screen.</summary>
public sealed record PulseFeedbackRequest(bool Helpful);
