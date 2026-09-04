using Nexo.Domain.Transactions;

namespace Nexo.Application.Deduplication;

/// <summary>
/// Pure matching logic — no database, no clock, no I/O — so every rule in it can be
/// tested directly. The service layer only supplies the candidate window.
///
/// The rules, in order:
///   1. Same fingerprint            -> ExactMatch (the fingerprint already encodes the recipe).
///   2. Same bank reference         -> ExactMatch (a reference identifies the movement at the bank).
///   3. Amount + date + description -> ProbableMatch above a score threshold.
/// A probable match never deletes anything: the caller keeps the row for review.
/// </summary>
public sealed class DeduplicationMatcher(DeduplicationOptions options)
{
    private readonly DeduplicationOptions _options = options;

    /// <summary>Lightweight projection of an existing movement, so the matcher never needs the entity.</summary>
    public sealed record ExistingMovement(
        Guid Id,
        Guid FinancialAccountId,
        DateTimeOffset TransactionDate,
        decimal Amount,
        TransactionDirection Direction,
        string? ExternalReference,
        string NormalizedDescription,
        string Fingerprint,
        TransactionStatus Status);

    public DuplicateCheckResult Match(IncomingMovement incoming, IReadOnlyList<ExistingMovement> candidates)
    {
        DuplicateMatch? best = null;

        foreach (var candidate in candidates)
        {
            if (candidate.FinancialAccountId != incoming.FinancialAccountId)
            {
                continue;
            }

            if (candidate.Status == TransactionStatus.Ignored)
            {
                continue;
            }

            if (string.Equals(candidate.Fingerprint, incoming.Fingerprint, StringComparison.Ordinal))
            {
                return new DuplicateCheckResult(
                    DuplicateMatchType.ExactMatch,
                    new DuplicateMatch(candidate.Id, DuplicateMatchType.ExactMatch, 1d, "same_fingerprint"));
            }

            if (TransactionFingerprint.HasUsableReference(incoming.ExternalReference)
                && TransactionFingerprint.HasUsableReference(candidate.ExternalReference)
                && string.Equals(
                    candidate.ExternalReference!.Trim(),
                    incoming.ExternalReference!.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return new DuplicateCheckResult(
                    DuplicateMatchType.ExactMatch,
                    new DuplicateMatch(candidate.Id, DuplicateMatchType.ExactMatch, 1d, "same_bank_reference"));
            }

            var probable = ScoreProbable(incoming, candidate);
            if (probable is not null && (best is null || probable.Score > best.Score))
            {
                best = probable;
            }
        }

        return best is null
            ? DuplicateCheckResult.None
            : new DuplicateCheckResult(DuplicateMatchType.ProbableMatch, best);
    }

    private DuplicateMatch? ScoreProbable(IncomingMovement incoming, ExistingMovement candidate)
    {
        if (candidate.Direction != incoming.Direction)
        {
            return null;
        }

        var amountScore = AmountScore(incoming.Amount, candidate.Amount);
        if (amountScore <= 0d)
        {
            return null;
        }

        var dayGap = Math.Abs((incoming.TransactionDate.UtcDateTime.Date - candidate.TransactionDate.UtcDateTime.Date).TotalDays);
        if (dayGap > _options.DateWindowDays)
        {
            return null;
        }

        var dateScore = 1d - (dayGap / Math.Max(1d, _options.DateWindowDays + 1d));

        var descriptionScore = TextNormalizer.Similarity(
            incoming.NormalizedDescription,
            candidate.NormalizedDescription);

        if (descriptionScore < _options.MinimumDescriptionSimilarity)
        {
            return null;
        }

        var score = (amountScore * 0.40d) + (dateScore * 0.25d) + (descriptionScore * 0.35d);

        return score >= _options.ProbableThreshold
            ? new DuplicateMatch(candidate.Id, DuplicateMatchType.ProbableMatch, Math.Round(score, 4), "amount_date_description")
            : null;
    }

    private double AmountScore(decimal incoming, decimal existing)
    {
        if (incoming == existing)
        {
            return 1d;
        }

        var reference = Math.Max(Math.Abs(incoming), Math.Abs(existing));
        if (reference == 0m)
        {
            return 0d;
        }

        var relativeDelta = Math.Abs(incoming - existing) / reference;
        if (relativeDelta > _options.AmountTolerance)
        {
            return 0d;
        }

        return 1d - (double)(relativeDelta / _options.AmountTolerance) * 0.3d;
    }
}
