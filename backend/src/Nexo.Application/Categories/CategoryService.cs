using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Domain.Categories;
using Nexo.Domain.Common;

namespace Nexo.Application.Categories;

public sealed record CategoryDto(
    Guid Id,
    string Code,
    string Name,
    string Icon,
    string Color,
    bool IsSystem,
    bool IsIncome);

/// <summary>Categorías personalizadas: Icon/Color must be one of <see cref="CategoryAppearance"/>'s curated options.</summary>
public sealed record CreateCategoryRequest(string Name, string Icon, string Color, bool IsIncome = false);

/// <summary>Categorías personalizadas: renaming/re-coloring never touches <see cref="IsIncome"/> -- changing income/expense after movements may already have been filtered by it is its own decision, not bundled into a simple appearance edit.</summary>
public sealed record UpdateCategoryRequest(string Name, string Icon, string Color);

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Creates a category owned by this user. 409 (conflict) if the name already belongs to a system category or one of this user's own.</summary>
    Task<CategoryDto> CreateAsync(Guid userId, CreateCategoryRequest request, CancellationToken cancellationToken);

    /// <summary>Renames/re-icons/re-colors a category this user owns. 404 if it does not exist, is not theirs, or is a system category.</summary>
    Task<CategoryDto> UpdateAsync(Guid userId, Guid categoryId, UpdateCategoryRequest request, CancellationToken cancellationToken);
}

public sealed class CategoryService(INexoDbContext db, IClock clock) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryDto>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Categories
            .AsNoTracking()
            .Where(c => c.UserId == null || c.UserId == userId)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Code, c.Name, c.Icon, c.Color, c.IsSystem, c.IsIncome))
            .ToListAsync(cancellationToken);

    public async Task<CategoryDto> CreateAsync(Guid userId, CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var name = DomainException.RequireText(request.Name, nameof(request.Name), 60);
        RequireValidIcon(request.Icon);
        RequireValidColor(request.Color);
        await RequireUniqueNameAsync(userId, name, existingCategoryId: null, cancellationToken);

        var category = Category.ForUser(userId, name, request.Icon, request.Color, clock.UtcNow, request.IsIncome);
        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);

        return Map(category);
    }

    public async Task<CategoryDto> UpdateAsync(
        Guid userId,
        Guid categoryId,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        // Never null||userId here (unlike ListAsync/RequireCategoryAsync elsewhere):
        // a system category must 404, not succeed, if someone tries to edit it.
        var category = await db.Categories.FirstOrDefaultAsync(
                c => c.Id == categoryId && c.UserId == userId,
                cancellationToken)
            ?? throw new NotFoundException("Category", categoryId);

        var name = DomainException.RequireText(request.Name, nameof(request.Name), 60);
        RequireValidIcon(request.Icon);
        RequireValidColor(request.Color);
        await RequireUniqueNameAsync(userId, name, existingCategoryId: category.Id, cancellationToken);

        category.UpdateAppearance(name, request.Icon, request.Color, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);

        return Map(category);
    }

    private static void RequireValidIcon(string icon) =>
        DomainException.Require(
            CategoryAppearance.Icons.Contains(icon),
            $"\"{icon}\" no es un ícono disponible para categorías.");

    private static void RequireValidColor(string color) =>
        DomainException.Require(
            CategoryAppearance.Colors.Contains(color),
            $"\"{color}\" no es un color disponible para categorías.");

    /// <summary>
    /// A user's own category name must not collide, case-insensitively, with a
    /// system category or another one of their own -- never a silent second
    /// "Comida" the picker can't tell apart from the real one.
    /// </summary>
    private async Task RequireUniqueNameAsync(
        Guid userId,
        string name,
        Guid? existingCategoryId,
        CancellationToken cancellationToken)
    {
        var query = db.Categories.Where(c => c.UserId == null || c.UserId == userId);

        if (existingCategoryId is { } id)
        {
            query = query.Where(c => c.Id != id);
        }

        var normalized = name.ToLowerInvariant();
        var conflict = await query.FirstOrDefaultAsync(c => c.Name.ToLower() == normalized, cancellationToken);

        if (conflict is not null)
        {
            throw new ConflictException($"Ya tienes una categoría llamada \"{conflict.Name}\".");
        }
    }

    private static CategoryDto Map(Category category) =>
        new(category.Id, category.Code, category.Name, category.Icon, category.Color, category.IsSystem, category.IsIncome);
}
