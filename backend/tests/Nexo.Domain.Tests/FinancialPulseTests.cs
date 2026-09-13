using Nexo.Domain.Common;
using Nexo.Domain.Pulses;
using Xunit;

namespace Nexo.Domain.Tests;

/// <summary>
/// PULSO FASE 1: FinancialPulse is a permanent event record (unlike
/// <see cref="Nexo.Domain.Insights.Insight"/>, which RecomputeAsync throws away
/// and rebuilds every pass), so its constructor is what guarantees the
/// dedup/relevance fields PulseEngine depends on are never left unset.
/// </summary>
public class FinancialPulseTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.CreateVersion7();

    private static FinancialPulse CreateValid(
        string title = "Gasto fuera de lo común",
        string body = "Un movimiento fue notablemente más alto de lo típico.",
        string explanation = "Comparamos contra tu gasto típico de los últimos meses.",
        string dedupKey = "unusual_expense:abc") =>
        FinancialPulse.Create(
            User,
            PulseType.UnusualExpense,
            PulseSeverity.Attention,
            title,
            body,
            explanation,
            dedupKey,
            relevanceScore: 62.5m,
            periodStart: Now.AddDays(-1),
            periodEnd: Now,
            occurredAt: Now.AddHours(-3),
            now: Now,
            value: 150m,
            comparisonValue: 40m,
            percentChange: 275m,
            referenceId: Guid.CreateVersion7().ToString());

    [Fact]
    public void A_pulse_created_with_valid_data_carries_every_field_PulseEngine_depends_on()
    {
        var pulse = CreateValid();

        Assert.Equal(User, pulse.UserId);
        Assert.Equal(PulseType.UnusualExpense, pulse.Type);
        Assert.Equal(PulseSeverity.Attention, pulse.Severity);
        Assert.Equal("unusual_expense:abc", pulse.DedupKey);
        Assert.Equal(62.5m, pulse.RelevanceScore);
        Assert.Equal(Now, pulse.CreatedAt);
        Assert.Equal(Now, pulse.UpdatedAt);
        Assert.Equal(150m, pulse.Value);
        Assert.Equal(40m, pulse.ComparisonValue);
        Assert.Equal(275m, pulse.PercentChange);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_pulse_cannot_be_created_without_a_title(string? title)
    {
        Assert.Throws<DomainException>(() => CreateValid(title: title!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_pulse_cannot_be_created_without_a_dedup_key(string dedupKey)
    {
        Assert.Throws<DomainException>(() => CreateValid(dedupKey: dedupKey));
    }

    [Fact]
    public void Text_longer_than_the_column_limit_is_truncated_not_rejected()
    {
        // Same convention as every other Entity.Create in this codebase
        // (DomainException.RequireText): a field that is merely too long is
        // truncated to fit its column, never a reason to reject the whole pulse.
        var pulse = CreateValid(explanation: new string('a', 600));

        Assert.Equal(500, pulse.Explanation.Length);
    }

    [Fact]
    public void A_new_pulse_carries_no_feedback_yet()
    {
        var pulse = CreateValid();

        Assert.Null(pulse.FeedbackHelpful);
        Assert.Null(pulse.FeedbackAt);
    }

    [Fact]
    public void Recording_feedback_sets_both_the_value_and_when()
    {
        var pulse = CreateValid();

        pulse.RecordFeedback(true, Now.AddHours(2));

        Assert.True(pulse.FeedbackHelpful);
        Assert.Equal(Now.AddHours(2), pulse.FeedbackAt);
    }

    [Fact]
    public void Changing_your_mind_replaces_the_previous_feedback()
    {
        var pulse = CreateValid();

        pulse.RecordFeedback(true, Now);
        pulse.RecordFeedback(false, Now.AddMinutes(5));

        Assert.False(pulse.FeedbackHelpful);
        Assert.Equal(Now.AddMinutes(5), pulse.FeedbackAt);
    }
}
