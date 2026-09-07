using Nexo.Domain.Common;

namespace Nexo.Domain.Categories;

/// <summary>
/// System categories (UserId == null) ship with Nexo and are shared by everyone.
/// A user may add their own; the query layer always returns "system OR mine".
/// </summary>
public sealed class Category : Entity
{
    private Category()
    {
    }

    public Guid? UserId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string Icon { get; private set; } = "circle";

    public string Color { get; private set; } = "#8DD9B6";

    public bool IsSystem { get; private set; }

    /// <summary>Income categories are excluded from spending breakdowns.</summary>
    public bool IsIncome { get; private set; }

    public int DisplayOrder { get; private set; }

    public static Category Builtin(
        string code,
        string name,
        string icon,
        string color,
        DateTimeOffset now,
        bool isIncome = false,
        int displayOrder = 0)
    {
        var category = new Category
        {
            Code = DomainException.RequireText(code, nameof(code), 40).ToUpperInvariant(),
            Name = DomainException.RequireText(name, nameof(name), 60),
            Icon = icon,
            Color = color,
            IsSystem = true,
            IsIncome = isIncome,
            DisplayOrder = displayOrder,
        };
        category.Stamp(now);
        return category;
    }

    public static Category ForUser(Guid userId, string name, string icon, string color, DateTimeOffset now)
    {
        var category = new Category
        {
            UserId = userId,
            Code = TransactionsCodeFrom(name),
            Name = DomainException.RequireText(name, nameof(name), 60),
            Icon = icon,
            Color = color,
            IsSystem = false,
        };
        category.Stamp(now);
        return category;
    }

    private static string TransactionsCodeFrom(string name)
    {
        // categories.Code is capped at 40 in the schema; keep the two in step.
        var normalized = DomainException.RequireText(name, nameof(name), 40).ToUpperInvariant();
        var chars = normalized.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars);
    }
}

/// <summary>Well-known system category codes, referenced by the rules engine and the seed.</summary>
public static class CategoryCodes
{
    public const string Food = "COMIDA";
    public const string Groceries = "SUPERMERCADO";
    public const string Transport = "TRANSPORTE";
    public const string Utilities = "SERVICIOS";
    public const string Entertainment = "ENTRETENIMIENTO";
    public const string Health = "SALUD";
    public const string Education = "EDUCACION";
    public const string Shopping = "COMPRAS";
    public const string Subscriptions = "SUSCRIPCIONES";
    public const string Transfers = "TRANSFERENCIAS";
    public const string Fees = "COMISIONES_IMPUESTOS";
    public const string Income = "INGRESOS";
    public const string Other = "OTROS";

    public static readonly string[] All =
    [
        Food, Groceries, Transport, Utilities, Entertainment, Health,
        Education, Shopping, Subscriptions, Transfers, Fees, Income, Other,
    ];
}
