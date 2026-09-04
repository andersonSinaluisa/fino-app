using Nexo.Domain.Transactions;

namespace Nexo.Application.Deduplication;

/// <summary>A movement that wants to enter the ledger, whatever channel produced it.</summary>
public sealed record IncomingMovement(
    Guid FinancialAccountId,
    string ProviderCode,
    DateTimeOffset TransactionDate,
    decimal Amount,
    TransactionDirection Direction,
    string Description,
    string? ExternalReference,
    string Fingerprint)
{
    public string NormalizedDescription { get; } = TextNormalizer.NormalizeForMatching(Description);
}

/// <summary>
/// Why a match was declared. Surfaced to the user in the import preview and kept on
/// the import row, because "we hid 3 movements" is only acceptable if we can say why.
/// </summary>
public sealed record DuplicateMatch(
    Guid TransactionId,
    DuplicateMatchType MatchType,
    double Score,
    string Reason);

public sealed record DuplicateCheckResult(DuplicateMatchType MatchType, DuplicateMatch? Match)
{
    public static readonly DuplicateCheckResult None = new(DuplicateMatchType.NoMatch, null);

    public bool IsExact => MatchType == DuplicateMatchType.ExactMatch;

    public bool IsProbable => MatchType == DuplicateMatchType.ProbableMatch;
}

/// <summary>Tunable without a deploy; defaults come from configuration.</summary>
public sealed class DeduplicationOptions
{
    public const string SectionName = "Nexo:Deduplication";

    /// <summary>How far apart two records of the same movement may be dated.</summary>
    public int DateWindowDays { get; set; } = 3;

    /// <summary>Relative amount tolerance (0.02 = 2%), for tips and currency rounding.</summary>
    public decimal AmountTolerance { get; set; } = 0.02m;

    /// <summary>Combined score at or above which a pair is a probable duplicate.</summary>
    public double ProbableThreshold { get; set; } = 0.72d;

    /// <summary>Below this description similarity a pair is never probable, whatever the amount.</summary>
    public double MinimumDescriptionSimilarity { get; set; } = 0.30d;
}

public interface IDeduplicationService
{
    /// <summary>Checks one movement against what the user already has in that account.</summary>
    Task<DuplicateCheckResult> CheckAsync(
        Guid userId,
        IncomingMovement movement,
        CancellationToken cancellationToken);

    /// <summary>
    /// Batch variant used by imports: loads the candidate window once and also
    /// deduplicates the incoming rows against each other.
    /// </summary>
    Task<IReadOnlyList<DuplicateCheckResult>> CheckBatchAsync(
        Guid userId,
        IReadOnlyList<IncomingMovement> movements,
        CancellationToken cancellationToken);
}
