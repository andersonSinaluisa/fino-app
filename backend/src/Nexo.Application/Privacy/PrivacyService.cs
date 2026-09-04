using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Domain.Audit;
using Nexo.Domain.Common;

namespace Nexo.Application.Privacy;

public sealed record DeleteTransactionsRequest(Guid? FinancialAccountId, DateTimeOffset? Before);

public sealed record DeletionSummary(int TransactionsDeleted, int AccountsDeleted, int ImportsDeleted);

public interface IPrivacyService
{
    /// <summary>Everything Nexo holds about the user, as a JSON document they can keep.</summary>
    Task<string> ExportAsync(Guid userId, CancellationToken cancellationToken);

    Task<DeletionSummary> DeleteTransactionsAsync(Guid userId, DeleteTransactionsRequest request, CancellationToken cancellationToken);

    Task<DeletionSummary> DeleteFinancialAccountAsync(Guid userId, Guid accountId, CancellationToken cancellationToken);

    Task RequestAccountDeletionAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class PrivacyService(INexoDbContext db, IClock clock) : IPrivacyService
{
    private static readonly JsonSerializerOptions ExportOptions = new()
    {
        WriteIndented = true,

        // Same casing as every other response, so a consumer of the export does
        // not have to special-case this one document.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<string> ExportAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                   ?? throw new NotFoundException("User", userId);

        var accounts = await db.FinancialAccounts.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => new
            {
                a.Id,
                a.ProviderCode,
                a.Alias,
                AccountType = a.AccountType.ToString(),
                a.Mask,
                a.Currency,
                ConnectionMode = a.ConnectionMode.ToString(),
                a.LastVerifiedBalance,
                a.LastVerifiedAt,
                a.EstimatedBalance,
                a.LastTransactionAt,
                a.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var transactions = await db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.TransactionDate)
            .Select(t => new
            {
                t.Id,
                t.FinancialAccountId,
                t.TransactionDate,
                t.Amount,
                Direction = t.Direction.ToString(),
                t.Currency,
                t.Description,
                t.Merchant,
                t.CategoryId,
                Source = t.Source.ToString(),
                Status = t.Status.ToString(),
                t.ExternalReference,
                t.Note,
                t.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var imports = await db.Imports.AsNoTracking()
            .Where(i => i.UserId == userId)
            .Select(i => new
            {
                i.Id,
                i.FileName,
                i.ParserCode,
                Status = i.Status.ToString(),
                i.TotalRows,
                i.ImportedCount,
                i.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var emailConnections = await db.EmailConnections.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => new
            {
                c.Id,
                Provider = c.ProviderKind.ToString(),
                c.EmailAddress,
                Status = c.Status.ToString(),
                c.ConnectedAt,
                c.LastSyncedAt,
                c.MessagesProcessed,
                c.TransactionsDetected,
            })
            .ToListAsync(cancellationToken);

        var payload = new
        {
            exportedAt = clock.UtcNow,
            user = new
            {
                user.Id,
                user.Email,
                user.DisplayName,
                user.TimeZoneId,
                user.PreferredCurrency,
                user.Locale,
                user.CreatedAt,
            },
            accounts,
            transactions,
            imports,
            emailConnections,
        };

        db.AuditLog.Add(AuditLogEntry.Record(
            userId,
            AuditActions.DataExported,
            "User",
            clock.UtcNow,
            userId.ToString()));

        await db.SaveChangesAsync(cancellationToken);

        return JsonSerializer.Serialize(payload, ExportOptions);
    }

    public async Task<DeletionSummary> DeleteTransactionsAsync(
        Guid userId,
        DeleteTransactionsRequest request,
        CancellationToken cancellationToken)
    {
        var query = db.Transactions.Where(t => t.UserId == userId);

        if (request.FinancialAccountId is { } accountId)
        {
            query = query.Where(t => t.FinancialAccountId == accountId);
        }

        if (request.Before is { } before)
        {
            query = query.Where(t => t.TransactionDate < before);
        }

        var transactions = await query.ToListAsync(cancellationToken);
        db.Transactions.RemoveRange(transactions);

        db.AuditLog.Add(AuditLogEntry.Record(
            userId,
            AuditActions.TransactionsDeleted,
            "Transaction",
            clock.UtcNow,
            request.FinancialAccountId?.ToString(),
            detail: $"count={transactions.Count}"));

        await db.SaveChangesAsync(cancellationToken);
        return new DeletionSummary(transactions.Count, 0, 0);
    }

    public async Task<DeletionSummary> DeleteFinancialAccountAsync(
        Guid userId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var account = await db.FinancialAccounts
            .FirstOrDefaultAsync(a => a.Id == accountId && a.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("FinancialAccount", accountId);

        var transactions = await db.Transactions
            .Where(t => t.UserId == userId && t.FinancialAccountId == accountId)
            .ToListAsync(cancellationToken);

        var imports = await db.Imports
            .Where(i => i.UserId == userId && i.FinancialAccountId == accountId)
            .ToListAsync(cancellationToken);

        var importIds = imports.Select(i => i.Id).ToArray();
        var rows = await db.ImportRows
            .Where(r => r.UserId == userId && importIds.Contains(r.ImportId))
            .ToListAsync(cancellationToken);

        db.Transactions.RemoveRange(transactions);
        db.ImportRows.RemoveRange(rows);
        db.Imports.RemoveRange(imports);
        db.FinancialAccounts.Remove(account);

        await db.SaveChangesAsync(cancellationToken);
        return new DeletionSummary(transactions.Count, 1, imports.Count);
    }

    public async Task RequestAccountDeletionAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                   ?? throw new NotFoundException("User", userId);

        var now = clock.UtcNow;
        user.RequestDeletion(now);

        // Sessions die immediately; the data erase is completed by the worker after
        // the grace period so an accidental request can still be undone.
        var tokens = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.Revoke("account_deletion_requested", now);
        }

        var connections = await db.EmailConnections.Where(c => c.UserId == userId).ToListAsync(cancellationToken);
        foreach (var connection in connections)
        {
            connection.Revoke(now);
        }

        db.AuditLog.Add(AuditLogEntry.Record(
            userId,
            AuditActions.AccountDeletionRequested,
            "User",
            now,
            userId.ToString()));

        await db.SaveChangesAsync(cancellationToken);
    }
}
