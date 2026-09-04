using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Accounts;
using Nexo.Application.Common;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Application.Insights;
using Nexo.Domain.Categories;
using Nexo.Domain.Common;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Transactions;

public interface ITransactionService
{
    Task<PagedResult<TransactionListItemDto>> QueryAsync(Guid userId, TransactionFilter filter, CancellationToken cancellationToken);

    Task<TransactionDetailDto> GetAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken);

    Task<TransactionDetailDto> UpdateCategoryAsync(Guid userId, Guid transactionId, UpdateCategoryRequest request, CancellationToken cancellationToken);

    Task<TransactionDetailDto> UpdateNoteAsync(Guid userId, Guid transactionId, UpdateNoteRequest request, CancellationToken cancellationToken);

    Task<HomeSummaryDto> GetHomeSummaryAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CategoryBreakdownItemDto>> GetCategoryBreakdownAsync(Guid userId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed class TransactionService(INexoDbContext db, IClock clock) : ITransactionService
{
    public async Task<PagedResult<TransactionListItemDto>> QueryAsync(
        Guid userId,
        TransactionFilter filter,
        CancellationToken cancellationToken)
    {
        var query = BuildQuery(userId, filter);

        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
        {
            return PagedResult<TransactionListItemDto>.Empty(filter.Page.NormalizedPageSize);
        }

        var rows = await query
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.CreatedAt)
            .Skip(filter.Page.Skip)
            .Take(filter.Page.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        var categories = await LoadCategoryLookupAsync(userId, cancellationToken);
        var brandColors = await LoadBrandColorsAsync(rows.Select(t => t.ProviderCode), cancellationToken);
        var aliases = await LoadAccountAliasesAsync(userId, rows.Select(t => t.FinancialAccountId), cancellationToken);

        var items = rows
            .Select(t => MapListItem(
                t,
                aliases.TryGetValue(t.FinancialAccountId, out var alias) ? alias : "Cuenta",
                brandColors,
                categories))
            .ToList();

        return new PagedResult<TransactionListItemDto>(
            items,
            filter.Page.NormalizedPage,
            filter.Page.NormalizedPageSize,
            total);
    }

    public async Task<TransactionDetailDto> GetAsync(
        Guid userId,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        var transaction = await RequireTransactionAsync(userId, transactionId, cancellationToken);
        return await MapDetailAsync(userId, transaction, cancellationToken);
    }

    public async Task<TransactionDetailDto> UpdateCategoryAsync(
        Guid userId,
        Guid transactionId,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var transaction = await RequireTransactionAsync(userId, transactionId, cancellationToken);

        var category = await db.Categories
            .FirstOrDefaultAsync(
                c => c.Id == request.CategoryId && (c.UserId == null || c.UserId == userId),
                cancellationToken)
            ?? throw new NotFoundException("Category", request.CategoryId);

        var now = clock.UtcNow;
        var previous = transaction.CategoryId;
        transaction.SetCategoryManually(category.Id, now);

        db.CategoryCorrections.Add(CategoryCorrection.Record(
            userId,
            transaction.Id,
            previous,
            category.Id,
            transaction.NormalizedDescription,
            now));

        // Learn the correction so the next movement from the same merchant is right
        // without asking again. One rule per (user, pattern, direction).
        if (request.CreateRule && !string.IsNullOrWhiteSpace(transaction.NormalizedDescription))
        {
            var pattern = BuildRulePattern(transaction.NormalizedDescription);
            if (pattern.Length >= 4)
            {
                var existing = await db.CategorizationRules.FirstOrDefaultAsync(
                    r => r.UserId == userId && r.Pattern == pattern && r.Direction == transaction.Direction,
                    cancellationToken);

                if (existing is null)
                {
                    db.CategorizationRules.Add(CategorizationRule.LearnedFromCorrection(
                        userId,
                        pattern,
                        category.Id,
                        transaction.Direction,
                        now));
                }
                else
                {
                    existing.Retarget(category.Id, now);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return await MapDetailAsync(userId, transaction, cancellationToken);
    }

    public async Task<TransactionDetailDto> UpdateNoteAsync(
        Guid userId,
        Guid transactionId,
        UpdateNoteRequest request,
        CancellationToken cancellationToken)
    {
        var transaction = await RequireTransactionAsync(userId, transactionId, cancellationToken);
        transaction.SetNote(request.Note, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return await MapDetailAsync(userId, transaction, cancellationToken);
    }

    public async Task<HomeSummaryDto> GetHomeSummaryAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                   ?? throw new NotFoundException("User", userId);

        var dates = new StatementDateInterpreter(user.TimeZoneId);
        var now = clock.UtcNow;
        var monthStart = dates.StartOfMonth(now);

        var accounts = await db.FinancialAccounts
            .AsNoTracking()
            .Where(a => a.UserId == userId && !a.IsArchived)
            .OrderBy(a => a.DisplayOrder)
            .ThenBy(a => a.Alias)
            .ToListAsync(cancellationToken);

        var providers = await db.Providers.AsNoTracking().ToListAsync(cancellationToken);
        var providerMap = providers.ToDictionary(p => p.Code, StringComparer.Ordinal);

        var accountDtos = accounts.Select(a => AccountService.Map(a, providerMap)).ToList();

        var monthlyQuery = db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.TransactionDate >= monthStart
                        && (t.Status == TransactionStatus.Posted || t.Status == TransactionStatus.Pending));

        var income = await monthlyQuery
            .Where(t => t.Direction == TransactionDirection.Income)
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

        var expense = await monthlyQuery
            .Where(t => t.Direction == TransactionDirection.Expense)
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

        var recent = await QueryAsync(
            userId,
            new TransactionFilter { Page = new PageRequest { Page = 1, PageSize = 8 } },
            cancellationToken);

        var breakdown = await GetCategoryBreakdownAsync(userId, monthStart, now, cancellationToken);

        var insights = await db.Insights
            .AsNoTracking()
            .Where(i => i.UserId == userId)
            .OrderBy(i => i.DisplayOrder)
            .Take(6)
            .Select(i => new InsightDto(
                i.Code,
                i.Title,
                i.Body,
                i.Value,
                i.ComparisonValue,
                i.PercentChange,
                i.Severity.ToString(),
                i.ReferenceId,
                i.PeriodStart,
                i.PeriodEnd))
            .ToListAsync(cancellationToken);

        return new HomeSummaryDto(
            Greeting(now, user.TimeZoneId),
            user.DisplayName,
            MoneyMath.Round(accounts.Sum(a => a.EstimatedBalance)),
            user.PreferredCurrency,
            accounts.Count,
            accounts.Any(a => a.BalanceKind == Nexo.Domain.Accounts.BalanceType.Estimated),
            new MonthlyTotalsDto(
                MoneyMath.Round(income),
                MoneyMath.Round(expense),
                MoneyMath.Round(income - expense)),
            accountDtos,
            recent.Items,
            breakdown,
            insights);
    }

    public async Task<IReadOnlyList<CategoryBreakdownItemDto>> GetCategoryBreakdownAsync(
        Guid userId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var grouped = await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.Direction == TransactionDirection.Expense
                        && t.TransactionDate >= from
                        && t.TransactionDate <= to
                        && (t.Status == TransactionStatus.Posted || t.Status == TransactionStatus.Pending))
            .GroupBy(t => t.CategoryId)
            .Select(g => new { CategoryId = g.Key, Total = g.Sum(x => x.Amount), Count = g.Count() })
            .ToListAsync(cancellationToken);

        if (grouped.Count == 0)
        {
            return [];
        }

        var categories = await LoadCategoryLookupAsync(userId, cancellationToken);
        var total = grouped.Sum(g => g.Total);

        return grouped
            .OrderByDescending(g => g.Total)
            .Select(g =>
            {
                var id = g.CategoryId ?? Guid.Empty;
                categories.TryGetValue(id, out var category);
                return new CategoryBreakdownItemDto(
                    id,
                    category?.Name ?? "Sin categoría",
                    category?.Icon ?? "circle",
                    category?.Color ?? "#ECE9E1",
                    MoneyMath.Round(g.Total),
                    total == 0m ? 0m : Math.Round(g.Total / total * 100m, 1),
                    g.Count);
            })
            .ToList();
    }

    private IQueryable<Transaction> BuildQuery(Guid userId, TransactionFilter filter)
    {
        var query = db.Transactions.AsNoTracking().Where(t => t.UserId == userId);

        if (!filter.IncludeIgnored)
        {
            query = query.Where(t => t.Status != TransactionStatus.Ignored);
        }

        if (filter.AccountId is { } accountId)
        {
            query = query.Where(t => t.FinancialAccountId == accountId);
        }

        if (filter.CategoryId is { } categoryId)
        {
            query = query.Where(t => t.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Direction)
            && Enum.TryParse<TransactionDirection>(filter.Direction, ignoreCase: true, out var direction))
        {
            query = query.Where(t => t.Direction == direction);
        }

        if (filter.From is { } from)
        {
            query = query.Where(t => t.TransactionDate >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(t => t.TransactionDate <= to);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = TextNormalizer.NormalizeForMatching(filter.Search);
            if (term.Length > 0)
            {
                query = query.Where(t => t.NormalizedDescription.Contains(term));
            }
        }

        return query;
    }

    private async Task<Transaction> RequireTransactionAsync(
        Guid userId,
        Guid transactionId,
        CancellationToken cancellationToken) =>
        await db.Transactions.FirstOrDefaultAsync(t => t.Id == transactionId && t.UserId == userId, cancellationToken)
        ?? throw new NotFoundException("Transaction", transactionId);

    private async Task<Dictionary<Guid, Category>> LoadCategoryLookupAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var categories = await db.Categories
            .AsNoTracking()
            .Where(c => c.UserId == null || c.UserId == userId)
            .ToListAsync(cancellationToken);

        return categories.ToDictionary(c => c.Id);
    }

    private async Task<Dictionary<string, string>> LoadBrandColorsAsync(
        IEnumerable<string> providerCodes,
        CancellationToken cancellationToken)
    {
        var codes = providerCodes.Distinct().ToArray();
        if (codes.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var providers = await db.Providers
            .AsNoTracking()
            .Where(p => codes.Contains(p.Code))
            .Select(p => new { p.Code, p.BrandColor })
            .ToListAsync(cancellationToken);

        return providers.ToDictionary(p => p.Code, p => p.BrandColor, StringComparer.Ordinal);
    }

    private static TransactionListItemDto MapListItem(
        Transaction transaction,
        string accountAlias,
        IReadOnlyDictionary<string, string> brandColors,
        IReadOnlyDictionary<Guid, Category> categories)
    {
        Category? category = null;
        if (transaction.CategoryId is { } categoryId)
        {
            categories.TryGetValue(categoryId, out category);
        }

        brandColors.TryGetValue(transaction.ProviderCode, out var color);

        return new TransactionListItemDto(
            transaction.Id,
            transaction.FinancialAccountId,
            accountAlias,
            transaction.ProviderCode,
            color ?? "#1D1D1B",
            transaction.TransactionDate,
            transaction.Amount,
            transaction.SignedAmount,
            transaction.Currency,
            transaction.Direction.ToString(),
            transaction.Description,
            transaction.Merchant,
            transaction.CategoryId,
            category?.Name,
            category?.Icon,
            category?.Color,
            transaction.Status.ToString(),
            transaction.Source.ToString());
    }

    private async Task<TransactionDetailDto> MapDetailAsync(
        Guid userId,
        Transaction transaction,
        CancellationToken cancellationToken)
    {
        var account = await db.FinancialAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == transaction.FinancialAccountId && a.UserId == userId, cancellationToken);

        var provider = await db.Providers
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == transaction.ProviderCode, cancellationToken);

        string? categoryName = null;
        if (transaction.CategoryId is { } categoryId)
        {
            categoryName = await db.Categories
                .AsNoTracking()
                .Where(c => c.Id == categoryId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new TransactionDetailDto(
            transaction.Id,
            transaction.FinancialAccountId,
            account?.Alias ?? "Cuenta",
            transaction.ProviderCode,
            provider?.Name ?? transaction.ProviderCode,
            transaction.AccountMask ?? account?.Mask,
            transaction.TransactionDate,
            transaction.Amount,
            transaction.SignedAmount,
            transaction.Currency,
            transaction.Direction.ToString(),
            transaction.Description,
            transaction.Merchant,
            transaction.CategoryId,
            categoryName,
            transaction.CategoryManuallySet,
            transaction.ExternalReference,
            transaction.Source.ToString(),
            transaction.SourceConfidence.ToString(),
            transaction.Status.ToString(),
            transaction.Note,
            transaction.PossibleDuplicateOfId,
            transaction.ImportId,
            transaction.CreatedAt);
    }

    private static string BuildRulePattern(string normalizedDescription)
    {
        var tokens = normalizedDescription
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .ToArray();

        return string.Join(' ', tokens);
    }

    private async Task<Dictionary<Guid, string>> LoadAccountAliasesAsync(
        Guid userId,
        IEnumerable<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        var ids = accountIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var accounts = await db.FinancialAccounts
            .AsNoTracking()
            .Where(a => a.UserId == userId && ids.Contains(a.Id))
            .Select(a => new { a.Id, a.Alias })
            .ToListAsync(cancellationToken);

        return accounts.ToDictionary(a => a.Id, a => a.Alias);
    }

    private static string Greeting(DateTimeOffset instant, string timeZoneId)
    {
        var localHour = new StatementDateInterpreter(timeZoneId).ToLocal(instant).Hour;

        return localHour switch
        {
            < 12 => "Buenos días",
            < 19 => "Buenas tardes",
            _ => "Buenas noches",
        };
    }
}
