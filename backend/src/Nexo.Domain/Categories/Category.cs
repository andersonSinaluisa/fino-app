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

    public static Category ForUser(
        Guid userId,
        string name,
        string icon,
        string color,
        DateTimeOffset now,
        bool isIncome = false)
    {
        var category = new Category
        {
            UserId = userId,
            Code = TransactionsCodeFrom(name),
            Name = DomainException.RequireText(name, nameof(name), 60),
            Icon = icon,
            Color = color,
            IsSystem = false,
            IsIncome = isIncome,
        };
        category.Stamp(now);
        return category;
    }

    /// <summary>
    /// Categorías personalizadas: rename/re-icon/re-color a category the person
    /// made themselves. Never touches <see cref="Code"/>'s role as a stable id for
    /// system categories because this can only ever run on a non-system one
    /// (<see cref="IsSystem"/> guard below) -- nothing outside this entity looks a
    /// user category up by its Code, so recomputing it on rename is purely cosmetic.
    /// </summary>
    public void UpdateAppearance(string name, string icon, string color, DateTimeOffset now)
    {
        DomainException.Require(!IsSystem, "No se puede editar una categoría del sistema.");

        Name = DomainException.RequireText(name, nameof(name), 60);
        Code = TransactionsCodeFrom(Name);
        Icon = icon;
        Color = color;
        Stamp(now);
    }

    private static string TransactionsCodeFrom(string name)
    {
        // categories.Code is capped at 40 in the schema; keep the two in step.
        var normalized = DomainException.RequireText(name, nameof(name), 40).ToUpperInvariant();
        var chars = normalized.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars);
    }
}

/// <summary>
/// Categorías personalizadas: the curated appearance options a person can pick
/// when creating or editing their own category. Icons are a small, font-
/// independent vocabulary (mobile resolves them via utils/categoryIcons.ts's
/// Ionicons map, the same one built-in categories already use -- see
/// ReferenceDataSeeder.SeedCategoriesAsync for the 13 already in use there).
/// Colors are exactly that seed's palette, so a user-made category can never
/// clash with FINO's deliberately-not-blue-fintech look. Both lists are
/// enforced server-side (CategoryService) -- never trust the client's icon/color
/// string just because it looks like a hex code or a known word.
/// </summary>
public static class CategoryAppearance
{
    public static readonly string[] Icons =
    [
        "utensils", "shopping-cart", "car", "zap", "film", "heart", "book",
        "shopping-bag", "repeat", "arrow-left-right", "percent", "trending-up", "circle",
        "home", "gift", "briefcase", "paw", "airplane", "cafe", "fitness",
        "musical-notes", "game-controller", "cash", "people", "construct",
    ];

    public static readonly string[] Colors =
    [
        "#E4A853", "#8DD9B6", "#7FB3E8", "#C7F36B", "#D8A0E8", "#D8665B",
        "#9AA8E8", "#E8B4A0", "#B6A0E8", "#ECE9E1", "#A67C52", "#4E9F73", "#74766F",
    ];
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

    /// <summary>
    /// Tarjetas de crédito: intereses de financiamiento y mora. A financial cost kept
    /// apart from consumption (and from Fees) so "¿cuánto me cuesta la tarjeta?" has
    /// its own answer.
    /// </summary>
    public const string Interest = "INTERESES";
    public const string Income = "INGRESOS";
    public const string Other = "OTROS";

    public static readonly string[] All =
    [
        Food, Groceries, Transport, Utilities, Entertainment, Health,
        Education, Shopping, Subscriptions, Transfers, Fees, Interest, Income, Other,
    ];
}
