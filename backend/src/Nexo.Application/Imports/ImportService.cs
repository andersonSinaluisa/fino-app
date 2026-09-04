using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexo.Application.Abstractions;
using Nexo.Application.Accounts;
using Nexo.Application.Categorization;
using Nexo.Application.Common;
using Nexo.Application.Deduplication;
using Nexo.Application.Imports.Parsing;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Application.Insights;
using Nexo.Domain.Accounts;
using Nexo.Domain.Audit;
using Nexo.Domain.Common;
using Nexo.Domain.Imports;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Imports;

public sealed class ImportOptions
{
    public const string SectionName = "Nexo:Imports";

    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    public int MaxRows { get; set; } = 20000;

    public string[] AllowedExtensions { get; set; } = [".csv", ".xlsx", ".txt"];
}

public interface IImportService
{
    Task<ImportPreviewDto> UploadAsync(
        Guid userId,
        Guid financialAccountId,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken);

    Task<ImportPreviewDto> GetPreviewAsync(Guid userId, Guid importId, CancellationToken cancellationToken);

    Task<ImportResultDto> ConfirmAsync(
        Guid userId,
        Guid importId,
        ConfirmImportRequest request,
        CancellationToken cancellationToken);

    Task CancelAsync(Guid userId, Guid importId, CancellationToken cancellationToken);
}

/// <summary>
/// The whole import flow: validate -> read -> detect parser -> parse -> normalise ->
/// deduplicate -> categorise -> preview -> (user confirms) -> write.
/// Nothing here knows any bank; the parser resolver does.
/// </summary>
public sealed class ImportService(
    INexoDbContext db,
    IStatementParserResolver parsers,
    IDeduplicationService deduplication,
    ICategorizationEngine categorization,
    IAccountService accounts,
    IInsightEngine insights,
    IRealtimeNotifier realtime,
    IClock clock,
    IOptions<ImportOptions> options,
    ILogger<ImportService> logger) : IImportService
{
    private readonly ImportOptions _options = options.Value;

    public async Task<ImportPreviewDto> UploadAsync(
        Guid userId,
        Guid financialAccountId,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken)
    {
        var account = await db.FinancialAccounts
            .FirstOrDefaultAsync(a => a.Id == financialAccountId && a.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("FinancialAccount", financialAccountId);

        var bytes = await ReadWithLimitAsync(content, _options.MaxFileSizeBytes, cancellationToken);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (!_options.AllowedExtensions.Contains(extension))
        {
            throw new UnsupportedFileException(
                $"Solo aceptamos archivos {string.Join(", ", _options.AllowedExtensions)}.");
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var now = clock.UtcNow;

        var import = Import.Start(userId, financialAccountId, fileName, contentType, bytes.Length, hash, now);
        db.Imports.Add(import);

        db.AuditLog.Add(AuditLogEntry.Record(
            userId,
            AuditActions.ImportUploaded,
            nameof(Import),
            now,
            import.Id.ToString()));

        TabularTable table;
        try
        {
            table = ReadTable(bytes, extension, _options.MaxRows);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or System.Xml.XmlException)
        {
            logger.LogWarning(ex, "Unreadable statement file for user {UserId}.", userId);
            import.MarkFailed("No pudimos leer el archivo. Verifica que sea un CSV o XLSX válido.", now);
            await db.SaveChangesAsync(cancellationToken);
            return await BuildPreviewAsync(userId, import, cancellationToken);
        }

        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, cancellationToken);
        var context = new StatementFileContext(
            fileName,
            contentType,
            table,
            account.ProviderCode,
            new StatementDateInterpreter(user.TimeZoneId));

        var parser = parsers.Resolve(context);
        if (parser is null)
        {
            import.MarkFailed(
                "No reconocimos el formato del archivo. Debe incluir columnas de fecha, descripción y valor.",
                now);
            await db.SaveChangesAsync(cancellationToken);
            return await BuildPreviewAsync(userId, import, cancellationToken);
        }

        var parsed = await parser.ParseAsync(context, cancellationToken);
        if (!parsed.Succeeded)
        {
            import.MarkFailed(parsed.FailureReason ?? "No pudimos procesar el archivo.", now);
            await db.SaveChangesAsync(cancellationToken);
            return await BuildPreviewAsync(userId, import, cancellationToken);
        }

        var movements = parsed.Transactions
            .Select(t => new IncomingMovement(
                financialAccountId,
                account.ProviderCode,
                t.TransactionDate,
                t.Amount,
                t.Direction,
                t.Description,
                t.ExternalReference,
                TransactionFingerprint.Compute(
                    account.ProviderCode,
                    financialAccountId,
                    t.ExternalReference,
                    t.TransactionDate,
                    t.Amount,
                    t.Direction,
                    t.Description)))
            .ToList();

        var matches = await deduplication.CheckBatchAsync(userId, movements, cancellationToken);
        var session = await categorization.StartSessionAsync(userId, cancellationToken);

        var rows = new List<ImportRow>();
        var newRows = 0;
        var duplicates = 0;
        var probable = 0;
        decimal income = 0m;
        decimal expense = 0m;

        for (var i = 0; i < parsed.Transactions.Count; i++)
        {
            var parsedRow = parsed.Transactions[i];
            var movement = movements[i];
            var match = matches[i];

            var row = ImportRow.Parsed(
                userId,
                import.Id,
                parsedRow.RowNumber,
                parsedRow.TransactionDate,
                parsedRow.Amount,
                parsedRow.Direction,
                parsedRow.Description,
                parsedRow.ExternalReference,
                movement.Fingerprint,
                parsedRow.RawPayload,
                now);

            if (match.Match is not null && match.MatchType != DuplicateMatchType.NoMatch)
            {
                row.MarkDuplicate(match.MatchType, match.Match.TransactionId, match.Match.Score, now);
            }

            var suggestion = session.Suggest(movement.NormalizedDescription, movement.Direction);
            row.SuggestCategory(suggestion?.CategoryId ?? session.FallbackCategoryId(movement.Direction), now);

            switch (match.MatchType)
            {
                case DuplicateMatchType.ExactMatch:
                    duplicates++;
                    break;
                case DuplicateMatchType.ProbableMatch:
                    probable++;
                    break;
                default:
                    newRows++;
                    break;
            }

            if (match.MatchType != DuplicateMatchType.ExactMatch)
            {
                if (parsedRow.Direction == TransactionDirection.Income)
                {
                    income += parsedRow.Amount;
                }
                else
                {
                    expense += parsedRow.Amount;
                }
            }

            rows.Add(row);
        }

        foreach (var error in parsed.Errors)
        {
            rows.Add(ImportRow.Rejected(userId, import.Id, error.RowNumber, error.Message, error.RawPayload, now));
        }

        db.ImportRows.AddRange(rows);

        import.MarkPreviewReady(
            parsed.ParserCode,
            parsed.Transactions.Count + parsed.Errors.Count,
            newRows,
            duplicates,
            probable,
            parsed.Errors.Count,
            income,
            expense,
            parsed.PeriodStart,
            parsed.PeriodEnd,
            parsed.DeclaredClosingBalance,
            now);

        await db.SaveChangesAsync(cancellationToken);
        await realtime.ImportProgressAsync(userId, import.Id, import.Status.ToString(), cancellationToken);

        return await BuildPreviewAsync(userId, import, cancellationToken);
    }

    public async Task<ImportPreviewDto> GetPreviewAsync(Guid userId, Guid importId, CancellationToken cancellationToken)
    {
        var import = await RequireImportAsync(userId, importId, cancellationToken);
        return await BuildPreviewAsync(userId, import, cancellationToken);
    }

    public async Task<ImportResultDto> ConfirmAsync(
        Guid userId,
        Guid importId,
        ConfirmImportRequest request,
        CancellationToken cancellationToken)
    {
        var import = await RequireImportAsync(userId, importId, cancellationToken);

        if (import.Status != ImportStatus.PreviewReady)
        {
            throw new ConflictException("Este archivo ya fue procesado o no está listo para confirmar.");
        }

        var account = await db.FinancialAccounts
            .FirstOrDefaultAsync(a => a.Id == import.FinancialAccountId && a.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("FinancialAccount", import.FinancialAccountId);

        var excluded = request.ExcludedRowIds?.ToHashSet() ?? [];
        var now = clock.UtcNow;

        var rows = await db.ImportRows
            .Where(r => r.ImportId == import.Id && r.UserId == userId)
            .OrderBy(r => r.RowNumber)
            .ToListAsync(cancellationToken);

        var imported = 0;
        var flagged = 0;

        foreach (var row in rows)
        {
            if (excluded.Contains(row.Id))
            {
                row.Skip(now);
                continue;
            }

            if (row.Status is ImportRowStatus.Invalid or ImportRowStatus.ExactDuplicate or ImportRowStatus.Skipped)
            {
                continue;
            }

            if (row.TransactionDate is not { } date
                || row.Amount is not { } amount
                || row.Direction is not { } direction
                || string.IsNullOrWhiteSpace(row.Description))
            {
                continue;
            }

            var transaction = Transaction.Create(
                userId,
                import.FinancialAccountId,
                account.ProviderCode,
                date,
                amount,
                direction,
                row.Description,
                TransactionSource.Import,
                now,
                account.Currency,
                row.ExternalReference,
                categoryId: row.SuggestedCategoryId == Guid.Empty ? null : row.SuggestedCategoryId,
                accountMask: account.Mask,
                confidence: SourceConfidence.High,
                importId: import.Id);

            // A probable duplicate is imported but parked for review: never silently
            // dropped, never silently double-counted.
            if (row.Status == ImportRowStatus.ProbableDuplicate && row.MatchedTransactionId is { } matchedId)
            {
                transaction.FlagAsPossibleDuplicate(matchedId, now);
                flagged++;
            }
            else
            {
                imported++;
            }

            db.Transactions.Add(transaction);
            row.MarkImported(transaction.Id, now);
        }

        // Both counters describe rows that were actually written; the DTO reports
        // them separately so the user knows how many need a look.
        import.MarkCompleted(imported + flagged, now);
        account.MarkSynced(now);

        if (request.ApplyDeclaredClosingBalance && import.DeclaredClosingBalance is { } closing)
        {
            account.SetVerifiedBalance(closing, import.PeriodEnd ?? now, now);
        }

        db.AuditLog.Add(AuditLogEntry.Record(
            userId,
            AuditActions.ImportConfirmed,
            nameof(Import),
            now,
            import.Id.ToString(),
            detail: $"imported={imported};flagged={flagged}"));

        await db.SaveChangesAsync(cancellationToken);

        await accounts.RecalculateBalanceAsync(userId, import.FinancialAccountId, cancellationToken);
        await insights.RecomputeAsync(userId, cancellationToken);
        await realtime.TransactionsChangedAsync(userId, imported + flagged, cancellationToken);

        var refreshed = await db.FinancialAccounts
            .AsNoTracking()
            .FirstAsync(a => a.Id == import.FinancialAccountId, cancellationToken);

        return new ImportResultDto(
            import.Id,
            imported,
            import.DuplicateRows,
            flagged,
            refreshed.EstimatedBalance,
            refreshed.BalanceKind.ToString());
    }

    public async Task CancelAsync(Guid userId, Guid importId, CancellationToken cancellationToken)
    {
        var import = await RequireImportAsync(userId, importId, cancellationToken);
        import.Cancel(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Import> RequireImportAsync(Guid userId, Guid importId, CancellationToken cancellationToken) =>
        await db.Imports.FirstOrDefaultAsync(i => i.Id == importId && i.UserId == userId, cancellationToken)
        ?? throw new NotFoundException("Import", importId);

    private async Task<ImportPreviewDto> BuildPreviewAsync(
        Guid userId,
        Import import,
        CancellationToken cancellationToken)
    {
        var rows = await db.ImportRows
            .AsNoTracking()
            .Where(r => r.ImportId == import.Id && r.UserId == userId)
            .OrderBy(r => r.RowNumber)
            .Take(500)
            .ToListAsync(cancellationToken);

        // Byte-identical file, same account, already confirmed: worth telling the
        // user before they re-read a preview full of duplicates.
        var previouslyImported = await db.Imports
            .AsNoTracking()
            .AnyAsync(
                i => i.UserId == userId
                     && i.FinancialAccountId == import.FinancialAccountId
                     && i.ContentHash == import.ContentHash
                     && i.Id != import.Id
                     && i.Status == ImportStatus.Completed,
                cancellationToken);

        var categoryIds = rows
            .Where(r => r.SuggestedCategoryId is not null)
            .Select(r => r.SuggestedCategoryId!.Value)
            .Distinct()
            .ToArray();

        var categoryNames = new Dictionary<Guid, string>();
        if (categoryIds.Length > 0)
        {
            categoryNames = await db.Categories
                .AsNoTracking()
                .Where(c => categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        }

        var rowDtos = rows.Select(r => new ImportPreviewRowDto(
            r.Id,
            r.RowNumber,
            r.TransactionDate,
            r.Amount,
            r.Direction?.ToString(),
            r.Description,
            r.ExternalReference,
            r.Status.ToString(),
            r.MatchType.ToString(),
            r.MatchedTransactionId,
            r.SuggestedCategoryId,
            r.SuggestedCategoryId is { } id && categoryNames.TryGetValue(id, out var name) ? name : null,
            r.Error)).ToList();

        return new ImportPreviewDto(
            import.Id,
            import.FinancialAccountId,
            import.FileName,
            import.ParserCode,
            import.Status.ToString(),
            import.TotalRows,
            import.NewRows,
            import.DuplicateRows,
            import.ProbableDuplicateRows,
            import.InvalidRows,
            import.IncomeTotal,
            import.ExpenseTotal,
            import.PeriodStart,
            import.PeriodEnd,
            import.DeclaredClosingBalance,
            import.FailureReason,
            previouslyImported,
            rowDtos);
    }

    internal static TabularTable ReadTable(byte[] bytes, string extension, int maxRows)
    {
        using var stream = new MemoryStream(bytes, writable: false);

        if (extension == ".xlsx" || XlsxTableReader.LooksLikeXlsx(bytes.AsSpan(0, Math.Min(4, bytes.Length))))
        {
            return XlsxTableReader.Read(stream, maxRows);
        }

        return CsvTableReader.Read(stream, maxRows);
    }

    private static async Task<byte[]> ReadWithLimitAsync(Stream content, long limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var pool = new byte[81920];
        long total = 0;

        while (true)
        {
            var read = await content.ReadAsync(pool, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > limit)
            {
                throw new UnsupportedFileException(
                    $"El archivo supera el límite de {limit / (1024 * 1024)} MB.");
            }

            await buffer.WriteAsync(pool.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }
}
