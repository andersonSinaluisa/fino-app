using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Domain.Audit;
using Nexo.Domain.Common;
using Nexo.Domain.Transactions;
using Nexo.Domain.Users;

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

        // OrderBy moved to after ToListAsync (LINQ to Objects), not in the EF query:
        // SQLite -- only the integration test host, never a real environment -- has
        // no native DateTimeOffset type and its EF Core provider explicitly does not
        // support ordering by one ("does not support expressions of type
        // 'DateTimeOffset' in ORDER BY clauses", per Microsoft's own SQLite provider
        // limitations doc). PostgreSQL/Npgsql handles it natively, but this export is
        // a one-off full dump of one user's data, never a hot path, so sorting the
        // already-small materialized list in memory costs nothing and works
        // identically on both providers instead of relying on one that only PostgreSQL
        // supports.
        var transactions = (await db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId)
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
                t.MerchantCorrected,
                t.IsInternalTransfer,
                t.InternalTransferLinkId,
                t.CategoryId,
                Source = t.Source.ToString(),
                Status = t.Status.ToString(),
                t.ExternalReference,
                t.Note,
                t.CreatedAt,
            })
            .ToListAsync(cancellationToken))
            .OrderBy(t => t.TransactionDate)
            .ToList();

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

        // Entregable 22 ("Privacidad completa"): this export claimed to be
        // "everything Nexo holds about the user" (see IPrivacyService.ExportAsync's
        // doc comment) since it was first written, but four whole categories of the
        // user's own data were missing until now.
        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => new { c.Id, c.Name, c.Icon, c.Color, c.IsIncome })
            .ToListAsync(cancellationToken);

        var categorizationRules = await db.CategorizationRules.AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => new
            {
                r.Id,
                r.Pattern,
                MatchKind = r.MatchKind.ToString(),
                r.CategoryId,
                Direction = r.Direction != null ? r.Direction.ToString() : null,
                r.TimesApplied,
                r.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var insights = await db.Insights.AsNoTracking()
            .Where(i => i.UserId == userId)
            .Select(i => new { i.Id, i.Code, i.Title, i.Body, i.PeriodStart, i.PeriodEnd, i.CreatedAt })
            .ToListAsync(cancellationToken);

        var notifications = await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .Select(n => new { n.Id, Type = n.Type.ToString(), n.Title, n.Body, IsRead = n.ReadAt != null, n.CreatedAt })
            .ToListAsync(cancellationToken);

        // Only the currently active sessions -- the same set the "Sesiones" screen
        // shows (Entregable 19). A rotated/revoked refresh token is internal
        // bookkeeping, not something the person would recognize as "their data",
        // and its hash is never exported regardless.
        var now = clock.UtcNow;
        var sessions = await db.IgnoringUserFilter<RefreshToken>().AsNoTracking()
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .Select(t => new { t.Id, t.DeviceLabel, t.UserAgent, t.CreatedAt, t.ExpiresAt })
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
            categories,
            categorizationRules,
            insights,
            notifications,
            sessions,
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
        await ClearDanglingTransferLinksAsync(userId, transactions, cancellationToken);
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

        // See QueryableGuidExtensions.WhereIdIn: `importIds.Contains(r.ImportId)`
        // combined with the global per-user filter does not translate on SQLite —
        // the same shape that broke import and the movements list. Deleting a
        // financial account with any import history hit this too.
        var importIds = imports.Select(i => i.Id).ToArray();
        var rows = await db.ImportRows
            .Where(r => r.UserId == userId)
            .WhereIdIn(r => r.ImportId, importIds)
            .ToListAsync(cancellationToken);

        await ClearDanglingTransferLinksAsync(userId, transactions, cancellationToken);
        db.Transactions.RemoveRange(transactions);
        db.ImportRows.RemoveRange(rows);
        db.Imports.RemoveRange(imports);
        db.FinancialAccounts.Remove(account);

        // Entregable 20: this was the one deletion path in this file with no
        // audit trail -- DeleteTransactionsAsync (above) and account-deletion
        // requests (below) both log theirs.
        db.AuditLog.Add(AuditLogEntry.Record(
            userId,
            AuditActions.AccountDeleted,
            "FinancialAccount",
            clock.UtcNow,
            accountId.ToString(),
            detail: $"transactions={transactions.Count},imports={imports.Count}"));

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

    /// <summary>
    /// Entregable 22: deleting one leg of a confirmed internal transfer (Entregable
    /// 13) must not leave the surviving leg pointing at a row that no longer
    /// exists -- InternalTransferLinkId has no FK (ADR-009: an OR-chain of Guid
    /// equality checks is the only shape this codebase trusts against SQLite once
    /// a global filter is in play, and that is exactly what this does, one link at
    /// a time). A dangling link would stay silently excluded from income/expense
    /// totals and insights forever, with no "clear transfer" affordance able to
    /// reach it, since InternalTransferService.ClearAsync resolves the *other* leg
    /// by id and would 404. Only the surviving leg needs clearing here: if both
    /// legs are in the same deletion batch, both rows are about to disappear
    /// together and there is nothing left to leave dangling.
    /// </summary>
    private async Task ClearDanglingTransferLinksAsync(
        Guid userId,
        IReadOnlyCollection<Transaction> removing,
        CancellationToken cancellationToken)
    {
        var removingIds = removing.Select(t => t.Id).ToHashSet();
        var danglingLinkIds = removing
            .Where(t => t.InternalTransferLinkId is { } linkedId && !removingIds.Contains(linkedId))
            .Select(t => t.InternalTransferLinkId!.Value)
            .Distinct();

        var now = clock.UtcNow;
        foreach (var linkedId in danglingLinkIds)
        {
            var linked = await db.Transactions
                .FirstOrDefaultAsync(t => t.Id == linkedId && t.UserId == userId, cancellationToken);
            linked?.ClearInternalTransfer(now);
        }
    }
}
