using Nexo.Domain.Common;

namespace Nexo.Domain.Categories;

/// <summary>
/// Every manual re-categorisation is recorded. Two reasons: it is the training set
/// for improving the rule set later, and it lets us measure how often the rules
/// are wrong without guessing.
/// </summary>
public sealed class CategoryCorrection : Entity, IUserOwned
{
    private CategoryCorrection()
    {
    }

    public Guid UserId { get; private set; }

    public Guid TransactionId { get; private set; }

    public Guid? PreviousCategoryId { get; private set; }

    public Guid NewCategoryId { get; private set; }

    public string NormalizedDescription { get; private set; } = null!;

    public static CategoryCorrection Record(
        Guid userId,
        Guid transactionId,
        Guid? previousCategoryId,
        Guid newCategoryId,
        string normalizedDescription,
        DateTimeOffset now)
    {
        var correction = new CategoryCorrection
        {
            UserId = userId,
            TransactionId = transactionId,
            PreviousCategoryId = previousCategoryId,
            NewCategoryId = newCategoryId,
            NormalizedDescription = normalizedDescription,
        };
        correction.Stamp(now);
        return correction;
    }
}
