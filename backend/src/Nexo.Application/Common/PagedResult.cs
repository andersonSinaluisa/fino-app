namespace Nexo.Application.Common;

/// <summary>
/// Offset pagination over a stable ordering (transaction date desc, then id).
///
/// ADR-005: keyset pagination is the better long-term answer for an append-heavy
/// feed, but it needs a single comparable sort column that every provider can
/// translate. The movements list is always filtered (account, period, category)
/// and the mobile client loads 30 rows at a time, so offsets stay small. The
/// sort column and a keyset switch are tracked as a follow-up rather than
/// pretended to be done.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public bool HasMore => Page * PageSize < TotalCount;

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public static PagedResult<T> Empty(int pageSize) => new([], 1, pageSize, 0);
}

public sealed record PageRequest
{
    private const int MaxPageSize = 100;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 30;

    public int NormalizedPage => Page < 1 ? 1 : Page;

    public int NormalizedPageSize => PageSize switch
    {
        < 1 => 30,
        > MaxPageSize => MaxPageSize,
        _ => PageSize,
    };

    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;
}
