namespace Nexo.Domain.Transactions;

/// <summary>
/// Money in or money out. The amount itself is always stored as a positive
/// magnitude so that reports never depend on a sign convention that differs
/// between banks.
/// </summary>
public enum TransactionDirection
{
    Income = 0,
    Expense = 1,
}

/// <summary>Where the movement physically came from.</summary>
public enum TransactionSource
{
    Import = 0,
    Email = 1,
    Api = 2,
    Webhook = 3,
    Manual = 4,
}

/// <summary>
/// How much Nexo trusts the parsed movement. Email notifications are usually a
/// heads-up (amount and merchant are reliable, the final posted amount may not be),
/// while an official statement row is authoritative.
/// </summary>
public enum SourceConfidence
{
    Low = 0,
    Medium = 1,
    High = 2,
}

public enum TransactionStatus
{
    /// <summary>Normal, visible movement that counts towards balances and insights.</summary>
    Posted = 0,

    /// <summary>Detected but not yet confirmed by a statement (typical for email-sourced rows).</summary>
    Pending = 1,

    /// <summary>Kept for traceability, excluded from totals: a probable duplicate awaiting user review.</summary>
    NeedsReview = 2,

    /// <summary>User dismissed it. Never deleted automatically.</summary>
    Ignored = 3,
}

/// <summary>Outcome of the deduplication check for an incoming movement.</summary>
public enum DuplicateMatchType
{
    NoMatch = 0,
    ProbableMatch = 1,
    ExactMatch = 2,
}
