namespace Nexo.Domain.Common;

/// <summary>
/// Base type for every persisted aggregate in Nexo.
/// Identifiers are UUID v7 so they stay sortable by creation time, which keeps
/// B-tree indexes on the transaction tables dense.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; protected set; }

    public DateTimeOffset UpdatedAt { get; protected set; }

    protected void Stamp(DateTimeOffset now)
    {
        if (CreatedAt == default)
        {
            CreatedAt = now;
        }

        UpdatedAt = now;
    }
}

/// <summary>
/// Marks an entity that belongs to exactly one Nexo user. Every type implementing
/// this interface gets an automatic per-user query filter in the DbContext, so a
/// missing <c>WHERE UserId = ...</c> in a handler cannot leak another user's money.
/// </summary>
public interface IUserOwned
{
    Guid UserId { get; }
}
