using Nexo.Domain.Common;

namespace Nexo.Domain.Pulses;

/// <summary>
/// PULSO: a single proactive, explainable observation about how the user's
/// money changed. Unlike <see cref="Insights.Insight"/> -- a replaceable
/// snapshot of "current state" that RecomputeAsync throws away and rebuilds
/// every pass -- a pulse is an event: once created it is a permanent record of
/// "this is what PulseEngine noticed, and when", the way a Notification is.
/// That is what lets a history screen exist later and what dedup/cooldown
/// checks against (see PulseEngine's remarks).
///
/// Every value on this record must trace back to a real computation over the
/// user's own transactions/accounts/categories -- PulseEngine never invents a
/// number or calls a random generator to decide relevance. "No genérico, no
/// gamificación": there is deliberately no streak counter, no points, no rank
/// here.
/// </summary>
public sealed class FinancialPulse : Entity, IUserOwned
{
    private FinancialPulse()
    {
    }

    public Guid UserId { get; private set; }

    public PulseType Type { get; private set; }

    public PulseSeverity Severity { get; private set; }

    /// <summary>QUÉ PASÓ, in one line -- what a PulseCard on Home would show.</summary>
    public string Title { get; private set; } = null!;

    public string Body { get; private set; } = null!;

    /// <summary>
    /// POR QUÉ -- the reasoning a Pulse detail screen shows under "¿Por qué veo
    /// esto?". Kept separate from <see cref="Body"/> so the card stays short
    /// while the detail screen can still be honest about the evidence.
    /// </summary>
    public string Explanation { get; private set; } = null!;

    public decimal? Value { get; private set; }

    public decimal? ComparisonValue { get; private set; }

    public decimal? PercentChange { get; private set; }

    /// <summary>Deep-link target: a transaction id, category id or account id, depending on <see cref="Type"/>.</summary>
    public string? ReferenceId { get; private set; }

    /// <summary>
    /// financialImpact + anomalyStrength + urgency + confidence − repetitionPenalty,
    /// computed once at creation time (see PulseEngine.ComputeRelevance). Higher
    /// means more worth a person's attention; this is what a future
    /// NotificationDecisionService and any "show at most N" UI order by.
    /// </summary>
    public decimal RelevanceScore { get; private set; }

    /// <summary>
    /// The anti-noise key: stable per rule and per "thing this pulse is about"
    /// (a transaction id for an event-shaped rule, "accountId" for a state-shaped
    /// one, "categoryId:yyyy-MM" for a monthly one). PulseEngine checks this
    /// before creating a new pulse -- see its remarks for the two dedup shapes.
    /// </summary>
    public string DedupKey { get; private set; } = null!;

    /// <summary>The window of data this observation is about (a day, a month).</summary>
    public DateTimeOffset PeriodStart { get; private set; }

    public DateTimeOffset PeriodEnd { get; private set; }

    /// <summary>When the underlying financial event happened, not when PulseEngine noticed it.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>
    /// PULSO FASE 4: the 👍/👎 a person can leave on the detail screen -- null
    /// until they do. Never fed back into relevance scoring or any rule
    /// (PulseEngine never reads it): this is product signal for later, not a
    /// live input, so recording it can never make PULSO itself "gamified" or
    /// self-tuning in a way that would be hard to explain.
    /// </summary>
    public bool? FeedbackHelpful { get; private set; }

    public DateTimeOffset? FeedbackAt { get; private set; }

    /// <summary>
    /// Records or replaces the person's feedback on this pulse -- changing
    /// your mind (👍 then 👎) is allowed, since a feedback tap is not a
    /// binding commitment.
    /// </summary>
    public void RecordFeedback(bool helpful, DateTimeOffset now)
    {
        FeedbackHelpful = helpful;
        FeedbackAt = now;
        Stamp(now);
    }

    public static FinancialPulse Create(
        Guid userId,
        PulseType type,
        PulseSeverity severity,
        string title,
        string body,
        string explanation,
        string dedupKey,
        decimal relevanceScore,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        DateTimeOffset occurredAt,
        DateTimeOffset now,
        decimal? value = null,
        decimal? comparisonValue = null,
        decimal? percentChange = null,
        string? referenceId = null)
    {
        var pulse = new FinancialPulse
        {
            UserId = userId,
            Type = type,
            Severity = severity,
            Title = DomainException.RequireText(title, nameof(title), 140),
            Body = DomainException.RequireText(body, nameof(body), 400),
            Explanation = DomainException.RequireText(explanation, nameof(explanation), 500),
            DedupKey = DomainException.RequireText(dedupKey, nameof(dedupKey), 160),
            RelevanceScore = relevanceScore,
            Value = value,
            ComparisonValue = comparisonValue,
            PercentChange = percentChange,
            ReferenceId = referenceId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            OccurredAt = occurredAt,
        };
        pulse.Stamp(now);
        return pulse;
    }
}
