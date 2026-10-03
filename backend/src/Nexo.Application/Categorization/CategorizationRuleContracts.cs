namespace Nexo.Application.Categorization;

/// <summary>
/// One row of "Reglas de categorización" (point 15). <see cref="Pattern"/> is
/// whichever match text the rule actually has -- the description pattern, or the
/// merchant pattern for a rule learnt through the older merchant-correction flow
/// (Entregable 14) -- so the screen has one column regardless of which dimension
/// the rule happens to use.
/// </summary>
public sealed record CategorizationRuleDto(
    Guid Id,
    string Pattern,
    string MatchType,
    Guid CategoryId,
    string CategoryName,
    string CategoryIcon,
    string CategoryColor,
    bool IsActive,
    int Priority,
    int MatchCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastMatchedAt);

/// <summary>
/// Point 14 (direct rule management, not through a transaction correction).
/// <paramref name="MatchType"/> is "Contains", "StartsWith" or "Exact" (point 4);
/// anything else is a validation error. The pattern is normalized and safety-
/// checked server-side regardless of what the client sends (point 18).
/// </summary>
public sealed record CreateCategorizationRuleRequest(string Pattern, string MatchType, Guid CategoryId);

/// <summary>
/// Point 16: changing a rule's category never touches past movements unless
/// <paramref name="ApplyToExistingMatches"/> is explicitly set. Pattern/MatchType
/// are immutable once created -- delete and recreate to change them.
/// </summary>
public sealed record UpdateCategorizationRuleRequest(Guid CategoryId, bool? IsActive, bool ApplyToExistingMatches = false);

public sealed record RulePreviewTransactionDto(Guid Id, string Description, decimal SignedAmount, DateTimeOffset TransactionDate);

/// <summary>
/// Point 19: "si creo esta regla, ¿qué movimientos coincidirían?", asked from a specific movement + chosen category.
/// <paramref name="Pattern"/> is optional: the part of the description the person chose
/// in the app ("CELLY AZANZA" instead of the suggested "CELLY"). It must be a piece of
/// this movement's own normalized description; null keeps the server's suggestion.
/// </summary>
public sealed record RulePreviewRequest(Guid TransactionId, Guid CategoryId, string? Pattern = null);

/// <summary>
/// <see cref="NormalizedDescription"/> is the exact text every rule is compared
/// against (upper-case, no accents, without reference numbers) so the app can show
/// which part of it <see cref="Pattern"/> is. <see cref="SuggestedPattern"/> is
/// what Fino would pick on its own, for "volver a la sugerencia".
/// </summary>
public sealed record RulePreviewDto(
    string Pattern,
    string MatchType,
    bool IsTooGeneric,
    int MatchedCount,
    IReadOnlyList<RulePreviewTransactionDto> Sample,
    Guid? ConflictingRuleId,
    Guid? ConflictingCategoryId,
    string? ConflictingCategoryName,
    string NormalizedDescription = "",
    string SuggestedPattern = "");
