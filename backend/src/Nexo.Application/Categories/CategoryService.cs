using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;

namespace Nexo.Application.Categories;

public sealed record CategoryDto(
    Guid Id,
    string Code,
    string Name,
    string Icon,
    string Color,
    bool IsSystem,
    bool IsIncome);

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> ListAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class CategoryService(INexoDbContext db) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryDto>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Categories
            .AsNoTracking()
            .Where(c => c.UserId == null || c.UserId == userId)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Code, c.Name, c.Icon, c.Color, c.IsSystem, c.IsIncome))
            .ToListAsync(cancellationToken);
}
