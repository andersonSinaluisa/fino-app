using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Domain.Accounts;
using Nexo.Domain.Audit;
using Nexo.Domain.Common;
using Nexo.Domain.Providers;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Accounts;

public interface IAccountService
{
    Task<IReadOnlyList<AccountDto>> ListAsync(Guid userId, bool includeArchived, CancellationToken cancellationToken);

    Task<AccountDto> GetAsync(Guid userId, Guid accountId, CancellationToken cancellationToken);

    Task<AccountDto> CreateAsync(Guid userId, CreateAccountRequest request, CancellationToken cancellationToken);

    Task<AccountDto> UpdateAsync(Guid userId, Guid accountId, UpdateAccountRequest request, CancellationToken cancellationToken);

    Task<AccountDto> SetVerifiedBalanceAsync(Guid userId, Guid accountId, SetVerifiedBalanceRequest request, CancellationToken cancellationToken);

    Task ArchiveAsync(Guid userId, Guid accountId, CancellationToken cancellationToken);

    /// <summary>Recomputes the estimate from stored movements. Called after imports and deletions.</summary>
    Task RecalculateBalanceAsync(Guid userId, Guid accountId, CancellationToken cancellationToken);
}

public sealed class AccountService(INexoDbContext db, IClock clock) : IAccountService
{
    public async Task<IReadOnlyList<AccountDto>> ListAsync(
        Guid userId,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var accounts = await db.FinancialAccounts
            .AsNoTracking()
            .Where(a => a.UserId == userId && (includeArchived || !a.IsArchived))
            .OrderBy(a => a.DisplayOrder)
            .ThenBy(a => a.Alias)
            .ToListAsync(cancellationToken);

        var providers = await LoadProvidersAsync(accounts.Select(a => a.ProviderCode), cancellationToken);
        return accounts.Select(a => Map(a, providers)).ToList();
    }

    public async Task<AccountDto> GetAsync(Guid userId, Guid accountId, CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(userId, accountId, cancellationToken);
        var providers = await LoadProvidersAsync([account.ProviderCode], cancellationToken);
        return Map(account, providers);
    }

    public async Task<AccountDto> CreateAsync(
        Guid userId,
        CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var code = (request.ProviderCode ?? string.Empty).Trim().ToUpperInvariant();
        var provider = await db.Providers.FirstOrDefaultAsync(p => p.Code == code && p.IsActive, cancellationToken)
                       ?? throw new NotFoundException("Provider", request.ProviderCode);

        if (!Enum.TryParse<AccountType>(request.AccountType, ignoreCase: true, out var accountType))
        {
            throw ValidationException.For(nameof(request.AccountType), "Tipo de cuenta no válido.");
        }

        if (!Enum.TryParse<ConnectionMode>(request.ConnectionMode, ignoreCase: true, out var mode))
        {
            throw ValidationException.For(nameof(request.ConnectionMode), "Modo de conexión no válido.");
        }

        if (!provider.Supports(mode))
        {
            throw ValidationException.For(
                nameof(request.ConnectionMode),
                $"{provider.Name} todavía no soporta ese modo de conexión.");
        }

        var now = clock.UtcNow;
        var order = await db.FinancialAccounts.CountAsync(a => a.UserId == userId, cancellationToken);

        // Onboarding funcional: "primera cuenta agregada" is a fact about the
        // account, not about which screen created it -- this fires the same way
        // whether it came from the onboarding flow or from Cuentas → Agregar
        // cuenta later. Guarded by `order == 0` so an already-onboarded user
        // adding their fifth account never pays for the extra lookup.
        if (order == 0)
        {
            var owner = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            owner?.RecordFirstAccountAdded(now);
        }

        var account = FinancialAccount.Open(
            userId,
            provider,
            request.Alias,
            accountType,
            mode,
            now,
            request.Mask,
            request.Currency,
            request.OpeningVerifiedBalance,
            order);

        db.FinancialAccounts.Add(account);
        db.AuditLog.Add(AuditLogEntry.Record(
            userId,
            AuditActions.AccountCreated,
            nameof(FinancialAccount),
            now,
            account.Id.ToString()));

        await db.SaveChangesAsync(cancellationToken);

        return Map(account, new Dictionary<string, Provider>(StringComparer.Ordinal) { [provider.Code] = provider });
    }

    public async Task<AccountDto> UpdateAsync(
        Guid userId,
        Guid accountId,
        UpdateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(userId, accountId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.Alias))
        {
            account.Rename(request.Alias, clock.UtcNow);
        }

        await db.SaveChangesAsync(cancellationToken);

        var providers = await LoadProvidersAsync([account.ProviderCode], cancellationToken);
        return Map(account, providers);
    }

    public async Task<AccountDto> SetVerifiedBalanceAsync(
        Guid userId,
        Guid accountId,
        SetVerifiedBalanceRequest request,
        CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(userId, accountId, cancellationToken);
        var now = clock.UtcNow;

        account.SetVerifiedBalance(request.Balance, request.AsOf ?? now, now);
        await db.SaveChangesAsync(cancellationToken);
        await RecalculateBalanceAsync(userId, accountId, cancellationToken);

        var providers = await LoadProvidersAsync([account.ProviderCode], cancellationToken);
        return Map(account, providers);
    }

    public async Task ArchiveAsync(Guid userId, Guid accountId, CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(userId, accountId, cancellationToken);
        var now = clock.UtcNow;

        account.Archive(now);
        db.AuditLog.Add(AuditLogEntry.Record(
            userId,
            AuditActions.AccountArchived,
            nameof(FinancialAccount),
            now,
            account.Id.ToString()));

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecalculateBalanceAsync(Guid userId, Guid accountId, CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(userId, accountId, cancellationToken);
        var anchor = account.LastVerifiedAt;

        var query = db.Transactions
            .Where(t => t.UserId == userId
                        && t.FinancialAccountId == accountId
                        && (t.Status == TransactionStatus.Posted || t.Status == TransactionStatus.Pending));

        if (anchor is not null)
        {
            query = query.Where(t => t.TransactionDate > anchor);
        }

        var income = await query
            .Where(t => t.Direction == TransactionDirection.Income)
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

        var expense = await query
            .Where(t => t.Direction == TransactionDirection.Expense)
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

        var lastTransactionAt = await db.Transactions
            .Where(t => t.UserId == userId && t.FinancialAccountId == accountId)
            .MaxAsync(t => (DateTimeOffset?)t.TransactionDate, cancellationToken);

        account.RebuildEstimate(income - expense, lastTransactionAt, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<FinancialAccount> RequireAccountAsync(
        Guid userId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var account = await db.FinancialAccounts
            .FirstOrDefaultAsync(a => a.Id == accountId && a.UserId == userId, cancellationToken);

        // The per-user query filter already scopes this, but the explicit UserId
        // predicate keeps the guarantee visible at the call site and in tests.
        return account ?? throw new NotFoundException("FinancialAccount", accountId);
    }

    private async Task<Dictionary<string, Provider>> LoadProvidersAsync(
        IEnumerable<string> codes,
        CancellationToken cancellationToken)
    {
        var distinct = codes.Distinct().ToArray();
        if (distinct.Length == 0)
        {
            return new Dictionary<string, Provider>(StringComparer.Ordinal);
        }

        var providers = await db.Providers
            .AsNoTracking()
            .Where(p => distinct.Contains(p.Code))
            .ToListAsync(cancellationToken);

        return providers.ToDictionary(p => p.Code, StringComparer.Ordinal);
    }

    internal static AccountDto Map(FinancialAccount account, IReadOnlyDictionary<string, Provider> providers)
    {
        providers.TryGetValue(account.ProviderCode, out var provider);

        return new AccountDto(
            account.Id,
            account.ProviderCode,
            provider?.Name ?? account.ProviderCode,
            provider?.BrandColor ?? "#1D1D1B",
            provider?.LogoKey,
            account.Alias,
            account.AccountType.ToString(),
            account.Mask,
            account.Currency,
            account.ConnectionMode.ToString(),
            account.EstimatedBalance,
            account.BalanceKind.ToString(),
            account.LastVerifiedBalance,
            account.LastVerifiedAt,
            account.LastTransactionAt,
            account.LastSyncedAt,
            account.IsArchived,
            account.IsLiability);
    }
}
