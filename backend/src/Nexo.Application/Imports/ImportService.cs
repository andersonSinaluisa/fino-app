using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexo.Application.Abstractions;
using Nexo.Application.Accounts;
using Nexo.Application.Categorization;
using Nexo.Application.Common;
using Nexo.Application.CreditCards;
using Nexo.Application.Deduplication;
using Nexo.Application.Imports.Parsing;
using Nexo.Application.Imports.Parsing.Parsers;
using Nexo.Application.Imports.Parsing.Tabular;
using Nexo.Application.Insights;
using Nexo.Domain.Accounts;
using Nexo.Domain.Audit;
using Nexo.Domain.Common;
using Nexo.Domain.CreditCards;
using Nexo.Domain.Imports;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Imports;

public sealed class ImportOptions
{
    public const string SectionName = "Nexo:Imports";

    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    public int MaxRows { get; set; } = 20000;

    // No real bank export needs this many columns; a defensive ceiling against a
    // file crafted to exhaust memory with an absurd number of delimiters on one
    // line, not a realistic width.
    public int MaxColumns { get; set; } = 200;

    // ".xls" is here for Banco Guayaquil's real export, which is an HTML document
    // saved with that extension (see HtmlTableReader) -- the actual file content
    // decides how it is read, this list only gates which extensions get that far.
    public string[] AllowedExtensions { get; set; } = [".csv", ".xlsx", ".txt", ".xls"];
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

    Task<PagedResult<ImportSummaryDto>> ListAsync(Guid userId, PageRequest page, CancellationToken cancellationToken);

    /// <summary>
    /// Entregable 10: the file the user re-sends after picking columns by hand
    /// because no parser recognised it on the first try (see the
    /// <c>unmappedColumns</c>/<c>unmappedSampleRows</c> hint on a Failed
    /// <see cref="ImportPreviewDto"/>). When <paramref name="saveMapping"/> is
    /// true the mapping is remembered for the account's provider, so the next
    /// statement from the same institution applies it automatically.
    /// </summary>
    Task<ImportPreviewDto> UploadManualAsync(
        Guid userId,
        Guid financialAccountId,
        string fileName,
        string contentType,
        Stream content,
        ManualColumnMapping mapping,
        bool saveMapping,
        CancellationToken cancellationToken);
}

/// <summary>User-supplied column choices for a file no parser could map on its own.</summary>
public sealed record ManualColumnMapping(
    bool FirstRowIsHeader,
    int DateColumn,
    int DescriptionColumn,
    int? AmountColumn,
    int? DebitColumn,
    int? CreditColumn,
    int? ReferenceColumn);

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
    ILogger<ImportService> logger,
    ICardMovementClassifier cardMovements,
    CreditCardService creditCards) : IImportService
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
        var (account, bytes, extension) =
            await LoadAccountAndFileAsync(userId, financialAccountId, fileName, content, cancellationToken);

        var now = clock.UtcNow;
        var import = StartImport(userId, financialAccountId, fileName, contentType, bytes, now);

        if (!TryReadTable(bytes, extension, userId, out var table, out var readError))
        {
            import.MarkFailed(readError!, now);
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
            // Entregable 10: before giving up, try a mapping this same user
            // already saved for this account's provider -- that is the whole
            // point of saving one. Only a genuinely new institution reaches the
            // "ask the user" branch below.
            var savedMapping = await db.ImportColumnMappings
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.UserId == userId && m.ProviderCode == account.ProviderCode, cancellationToken);

            if (savedMapping is not null)
            {
                var mappedParser = new ManualMappingStatementParser(
                    account.ProviderCode,
                    ToColumnMap(savedMapping),
                    savedMapping.FirstRowIsHeader);

                var mappedResult = await mappedParser.ParseAsync(context, cancellationToken);
                return await ApplyParsedStatementAsync(userId, import, account, mappedResult, now, cancellationToken, table);
            }

            import.MarkFailed(
                "No reconocimos el formato del archivo. Debe incluir columnas de fecha, descripción y valor.",
                now);
            await db.SaveChangesAsync(cancellationToken);

            // Entregable 10: hand the client enough of the raw file to build a
            // manual column picker -- capped well under MaxColumns so a hostile
            // file cannot turn this into an unbounded response either.
            var sampleRows = table.Rows.Take(4).Select(r => (IReadOnlyList<string>)r.Cells.Take(50).ToList()).ToList();
            return await BuildPreviewAsync(
                userId,
                import,
                cancellationToken,
                unmappedColumns: sampleRows.Count > 0 ? sampleRows[0] : null,
                unmappedSampleRows: sampleRows.Count > 1 ? sampleRows.Skip(1).ToList() : null);
        }

        var parsed = await parser.ParseAsync(context, cancellationToken);
        return await ApplyParsedStatementAsync(userId, import, account, parsed, now, cancellationToken, table);
    }

    public async Task<ImportPreviewDto> UploadManualAsync(
        Guid userId,
        Guid financialAccountId,
        string fileName,
        string contentType,
        Stream content,
        ManualColumnMapping mapping,
        bool saveMapping,
        CancellationToken cancellationToken)
    {
        var (account, bytes, extension) =
            await LoadAccountAndFileAsync(userId, financialAccountId, fileName, content, cancellationToken);

        var now = clock.UtcNow;
        var import = StartImport(userId, financialAccountId, fileName, contentType, bytes, now);

        if (!TryReadTable(bytes, extension, userId, out var table, out var readError))
        {
            import.MarkFailed(readError!, now);
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

        var columnMap = new StatementColumnMap
        {
            Date = mapping.DateColumn,
            Description = mapping.DescriptionColumn,
            Amount = mapping.AmountColumn ?? -1,
            Debit = mapping.DebitColumn ?? -1,
            Credit = mapping.CreditColumn ?? -1,
            Reference = mapping.ReferenceColumn ?? -1,
        };

        // The mapping's own invariants (required columns, at least one amount
        // source) are enforced by ImportColumnMapping.Create/Replace below when
        // saveMapping is true; when it is false we still must not accept a
        // mapping so broken every row would fail, so the same construction runs
        // either way -- just discarded afterwards if the user chose not to keep it.
        var mappingEntity = ImportColumnMapping.Create(
            userId,
            account.ProviderCode,
            mapping.FirstRowIsHeader,
            mapping.DateColumn,
            mapping.DescriptionColumn,
            mapping.AmountColumn,
            mapping.DebitColumn,
            mapping.CreditColumn,
            mapping.ReferenceColumn,
            now);

        if (saveMapping)
        {
            var existing = await db.ImportColumnMappings
                .FirstOrDefaultAsync(m => m.UserId == userId && m.ProviderCode == account.ProviderCode, cancellationToken);

            if (existing is null)
            {
                db.ImportColumnMappings.Add(mappingEntity);
            }
            else
            {
                existing.Replace(
                    mapping.FirstRowIsHeader,
                    mapping.DateColumn,
                    mapping.DescriptionColumn,
                    mapping.AmountColumn,
                    mapping.DebitColumn,
                    mapping.CreditColumn,
                    mapping.ReferenceColumn,
                    now);
            }
        }

        var manualParser = new ManualMappingStatementParser(account.ProviderCode, columnMap, mapping.FirstRowIsHeader);
        var parsed = await manualParser.ParseAsync(context, cancellationToken);

        return await ApplyParsedStatementAsync(userId, import, account, parsed, now, cancellationToken, table);
    }

    private static StatementColumnMap ToColumnMap(ImportColumnMapping mapping) => new()
    {
        Date = mapping.DateColumn,
        Description = mapping.DescriptionColumn,
        Amount = mapping.AmountColumn ?? -1,
        Debit = mapping.DebitColumn ?? -1,
        Credit = mapping.CreditColumn ?? -1,
        Reference = mapping.ReferenceColumn ?? -1,
    };

    private async Task<(FinancialAccount Account, byte[] Bytes, string Extension)> LoadAccountAndFileAsync(
        Guid userId,
        Guid financialAccountId,
        string fileName,
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

        return (account, bytes, extension);
    }

    private Import StartImport(
        Guid userId, Guid financialAccountId, string fileName, string contentType, byte[] bytes, DateTimeOffset now)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var import = Import.Start(userId, financialAccountId, fileName, contentType, bytes.Length, hash, now);
        db.Imports.Add(import);

        db.AuditLog.Add(AuditLogEntry.Record(
            userId,
            AuditActions.ImportUploaded,
            nameof(Import),
            now,
            import.Id.ToString()));

        return import;
    }

    private bool TryReadTable(
        byte[] bytes, string extension, Guid userId, out TabularTable table, out string? failureReason)
    {
        try
        {
            table = ReadTable(bytes, extension, _options.MaxRows, _options.MaxColumns);
            failureReason = null;
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or System.Xml.XmlException
                                        or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            logger.LogWarning(ex, "Unreadable statement file for user {UserId}.", userId);
            table = null!;
            failureReason = "No pudimos leer el archivo. Verifica que sea un CSV o XLSX válido.";
            return false;
        }
    }

    /// <summary>
    /// Everything from "a parser produced a StatementParseResult" onward:
    /// dedup, categorisation suggestion, row persistence, totals. Shared by the
    /// normal upload path, the saved-mapping auto-apply path, and the manual
    /// mapping path (Entregable 10) -- a manually mapped file is held to the
    /// same dedup/categorisation/balance logic as a recognised one, not a
    /// lesser one.
    /// </summary>
    private async Task<ImportPreviewDto> ApplyParsedStatementAsync(
        Guid userId,
        Import import,
        FinancialAccount account,
        StatementParseResult parsed,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        TabularTable? table = null)
    {
        if (!parsed.Succeeded)
        {
            import.MarkFailed(parsed.FailureReason ?? "No pudimos procesar el archivo.", now);
            await db.SaveChangesAsync(cancellationToken);
            return await BuildPreviewAsync(userId, import, cancellationToken);
        }

        // Tarjetas de crédito: same pipeline, two card-specific steps -- purchases
        // must raise the debt whatever sign convention the file used, and the
        // statement's header figures (corte, fecha máxima, total, mínimo, cupo) are
        // kept to declare the statement on confirm.
        if (account.IsLiability)
        {
            (parsed, _) = CreditCardStatementImport.Orient(parsed);
            if (table is not null && CreditCardStatementImport.ReadSummary(table) is { IsEmpty: false } summary)
            {
                import.AttachCardStatementSummary(
                    null,
                    summary.ClosingDate,
                    summary.DueDate,
                    summary.StatementBalance,
                    summary.MinimumPayment,
                    summary.CreditLimit,
                    now);
            }
        }

        var financialAccountId = account.Id;

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

        var matches = (await deduplication.CheckBatchAsync(userId, movements, cancellationToken)).ToList();
        await ReconcileCardPaymentLegsAsync(userId, account, movements, matches, cancellationToken);
        var coveredByPlans = account.IsLiability
            ? await FindInstallmentLinesCoveredByPlansAsync(userId, account, movements, cancellationToken)
            : new Dictionary<int, (Guid PurchaseId, string Reason)>();
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
            else if (coveredByPlans.TryGetValue(i, out var covered))
            {
                // Tarjetas de crédito: "CUOTA 4/12" of a purchase Fino already has
                // (with its plan). Left out on purpose and explained -- never counted twice.
                row.MarkCoveredByInstallmentPlan(covered.PurchaseId, covered.Reason, now);
                duplicates++;
                rows.Add(row);
                continue;
            }

            // Same merchant the resulting Transaction will actually store (see
            // Transaction.Create below), so a merchant rule matches exactly what the
            // person will later see and correct -- never a value computed differently.
            var normalizedMerchant = TextNormalizer.NormalizeForMatching(TextNormalizer.ExtractMerchant(movement.Description));
            var suggestion = session.Suggest(
                movement.NormalizedDescription,
                normalizedMerchant,
                movement.ProviderCode,
                movement.Amount,
                movement.Direction);

            if (suggestion is not null)
            {
                row.SuggestCategory(
                    suggestion.CategoryId,
                    now,
                    suggestion.RuleSource == "user_rule" ? CategorySource.UserRule : CategorySource.SystemRule,
                    suggestion.RuleId);
            }
            else
            {
                row.SuggestCategory(session.FallbackCategoryId(movement.Direction), now, CategorySource.Imported);
            }

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

        // Point 20 ("performance"): one bulk update for every rule this import
        // actually matched, not one write per row.
        await categorization.RecordHitsAsync(session, now, cancellationToken);

        import.MarkPreviewReady(
            parsed.ParserCode,
            parsed.DetectedProviderCode,
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
        // Entregable 28 ("Observabilidad"): a span around the write path of an
        // import -- the read-only preview (UploadAsync) is comparatively cheap and
        // already visible through the ASP.NET Core request itself; this is the part
        // that matters to know is slow or failing in production.
        using var activity = NexoTelemetry.ActivitySource.StartActivity("Import.Confirm");
        activity?.SetTag("nexo.import_id", importId);

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
        var upgraded = 0;

        foreach (var row in rows)
        {
            if (excluded.Contains(row.Id))
            {
                row.Skip(now);
                continue;
            }

            // Entregable 27 ("Dedup correo/importación"): an exact duplicate is the
            // bank statement restating a movement Nexo already has -- most often one
            // an email notification created earlier as Pending/Medium-confidence.
            // Before this, the statement's authoritative data (exact amount,
            // reference, final description) was simply thrown away and the earlier
            // row stayed Pending/Medium forever. Transaction.UpgradeFrom exists
            // precisely to fold that authoritative data in; it had no caller
            // anywhere in the application until now.
            if (row.Status == ImportRowStatus.ExactDuplicate)
            {
                if (row.MatchedTransactionId is { } exactMatchedTransactionId
                    && row.TransactionDate is { } matchedDate
                    && row.Amount is { } matchedAmount
                    && row.Direction is { } matchedDirection
                    && !string.IsNullOrWhiteSpace(row.Description))
                {
                    var existing = await db.Transactions
                        .FirstOrDefaultAsync(t => t.Id == exactMatchedTransactionId && t.UserId == userId, cancellationToken);

                    // A transaction the user already dismissed (Ignored) via the
                    // duplicate-review endpoint must stay dismissed: upgrading it
                    // here would silently resurrect a decision the user already made.
                    if (existing is not null && existing.Status != TransactionStatus.Ignored)
                    {
                        var authoritative = Transaction.Create(
                            userId,
                            import.FinancialAccountId,
                            account.ProviderCode,
                            matchedDate,
                            matchedAmount,
                            matchedDirection,
                            row.Description,
                            TransactionSource.Import,
                            now,
                            account.Currency,
                            row.ExternalReference,
                            accountMask: account.Mask,
                            confidence: SourceConfidence.High,
                            importId: import.Id);

                        existing.UpgradeFrom(authoritative, now);
                        upgraded++;
                        row.MarkImported(existing.Id, now);
                    }
                }

                continue;
            }

            if (row.Status is ImportRowStatus.Invalid or ImportRowStatus.Skipped)
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
                importId: import.Id,
                categorySource: row.SuggestedCategorySource,
                categorizationRuleId: row.SuggestedCategorizationRuleId);

            // Tarjetas de crédito: a card movement is stored with its type (compra,
            // pago, devolución, interés...), so the payment line of a card statement is
            // never read as income.
            await cardMovements.ApplyAsync(account, transaction, null, now, cancellationToken);

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

        // Three counters describe rows that were actually written (created or
        // upgraded in place); the DTO reports them separately so the user knows
        // how many need a look and how many were reconciled automatically.
        import.MarkCompleted(imported + flagged + upgraded, now);
        account.MarkSynced(now);

        // Tarjetas de crédito: the statement's official figures become the declared
        // statement of that closing (traceable to this import).
        if (account.IsLiability && import.HasCardStatementSummary)
        {
            await DeclareCardStatementAsync(userId, account, import, cancellationToken);
        }

        // A card's "saldo" in a file is not its debt in Fino's sign convention (and
        // a statement total excludes deferred installments), so it never anchors a card.
        if (request.ApplyDeclaredClosingBalance && !account.IsLiability && import.DeclaredClosingBalance is { } closing)
        {
            var asOf = import.PeriodEnd ?? now;

            // Never let an older statement's own closing balance regress a more
            // recent verified anchor -- e.g. this file is last month's export,
            // re-imported or imported out of order after this month's was already
            // confirmed. Anything older than the current anchor is superseded by
            // definition and must not overwrite it.
            if (account.LastVerifiedAt is null || asOf >= account.LastVerifiedAt)
            {
                account.SetVerifiedBalance(closing, asOf, now);
            }
        }

        db.AuditLog.Add(AuditLogEntry.Record(
            userId,
            AuditActions.ImportConfirmed,
            nameof(Import),
            now,
            import.Id.ToString(),
            detail: $"imported={imported};flagged={flagged};upgraded={upgraded}"));

        NexoTelemetry.ImportRowsProcessed.Add(imported, new KeyValuePair<string, object?>("outcome", "imported"));
        NexoTelemetry.ImportRowsProcessed.Add(flagged, new KeyValuePair<string, object?>("outcome", "flagged_for_review"));
        NexoTelemetry.ImportRowsProcessed.Add(upgraded, new KeyValuePair<string, object?>("outcome", "upgraded"));
        activity?.SetTag("nexo.imported", imported);
        activity?.SetTag("nexo.flagged", flagged);
        activity?.SetTag("nexo.upgraded", upgraded);

        // Onboarding funcional: "primera importación completada" is PULSO's
        // north-star metric (first successful import rate), so it is recorded
        // here -- the one place a confirm can actually be said to have
        // produced something -- rather than from whichever screen called
        // ConfirmAsync. Only counts when at least one row actually landed;
        // an import that was entirely duplicates/invalid rows never "shows
        // someone their money", so it must not silently satisfy the milestone.
        if (imported > 0)
        {
            var owner = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            owner?.RecordFirstImportCompleted(now);
        }

        await db.SaveChangesAsync(cancellationToken);

        await accounts.RecalculateBalanceAsync(userId, import.FinancialAccountId, cancellationToken);
        await insights.RecomputeAsync(userId, cancellationToken);
        await realtime.TransactionsChangedAsync(userId, imported + flagged + upgraded, cancellationToken);

        var refreshed = await db.FinancialAccounts
            .AsNoTracking()
            .FirstAsync(a => a.Id == import.FinancialAccountId, cancellationToken);

        return new ImportResultDto(
            import.Id,
            imported,
            import.DuplicateRows,
            flagged,
            refreshed.EstimatedBalance,
            refreshed.BalanceKind.ToString(),
            upgraded);
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

    /// <summary>
    /// Entregable 8: the history screen that lets the user see -- and the app
    /// confirm -- that a cancelled or completed import never stays orphaned as
    /// "pending" forever. Newest first, same offset-pagination shape as the
    /// movements list (see PagedResult's ADR-005 note).
    /// </summary>
    public async Task<PagedResult<ImportSummaryDto>> ListAsync(
        Guid userId,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var query = db.Imports.AsNoTracking().Where(i => i.UserId == userId);

        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
        {
            return PagedResult<ImportSummaryDto>.Empty(page.NormalizedPageSize);
        }

        var rows = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        var aliases = await LoadAccountAliasesAsync(userId, rows.Select(i => i.FinancialAccountId), cancellationToken);

        var items = rows
            .Select(i => new ImportSummaryDto(
                i.Id,
                i.FinancialAccountId,
                aliases.TryGetValue(i.FinancialAccountId, out var alias) ? alias : "Cuenta",
                i.FileName,
                i.Status.ToString(),
                i.CreatedAt,
                i.NewRows,
                i.DuplicateRows,
                i.ProbableDuplicateRows,
                i.InvalidRows,
                i.ImportedCount))
            .ToList();

        return new PagedResult<ImportSummaryDto>(items, page.NormalizedPage, page.NormalizedPageSize, total);
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

        // See QueryableGuidExtensions.WhereIdIn (ADR-009): `ids.Contains(a.Id)`
        // combined with the global per-user query filter does not translate on
        // SQLite, which is exactly what broke the movements list before it was
        // fixed there -- the same shape of query, so the same fix applies here.
        var accounts = await db.FinancialAccounts
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .WhereIdIn(a => a.Id, ids)
            .Select(a => new { a.Id, a.Alias })
            .ToListAsync(cancellationToken);

        return accounts.ToDictionary(a => a.Id, a => a.Alias);
    }

    /// <summary>
    /// Tarjetas de crédito, conciliación: a payment registered by hand ("Pagar
    /// tarjeta") has two legs -- "Pago tarjeta Visa" on the bank and "Pago desde
    /// Banco" on the card. When the bank's or the card's statement later lists that
    /// same payment ("PAGO TARJETA VISA", "SU PAGO GRACIAS"), the texts rarely look
    /// alike, so the description-based matcher can miss it. Same account, same
    /// direction, same amount, a few days apart, against a hand-registered leg of a
    /// card payment: kept as a PROBABLE duplicate for the person to confirm -- never
    /// merged automatically.
    /// </summary>
    private async Task ReconcileCardPaymentLegsAsync(
        Guid userId,
        FinancialAccount account,
        IReadOnlyList<IncomingMovement> movements,
        List<DuplicateCheckResult> matches,
        CancellationToken cancellationToken)
    {
        var candidates = Enumerable.Range(0, movements.Count)
            .Where(i => matches[i].MatchType == DuplicateMatchType.NoMatch
                        && (account.IsLiability
                            ? movements[i].Direction == TransactionDirection.Income
                              && CreditCardMovementRules.Classify(TransactionDirection.Income, movements[i].Description) == CreditCardMovementType.Payment
                            : movements[i].Direction == TransactionDirection.Expense))
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var window = TimeSpan.FromDays(CreditCardService.PaymentMatchWindowDays);
        var from = candidates.Min(i => movements[i].TransactionDate) - window;
        var to = candidates.Max(i => movements[i].TransactionDate) + window;

        var legs = await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.FinancialAccountId == account.Id
                        && t.Source == TransactionSource.Manual
                        && t.IsInternalTransfer
                        && t.TransactionDate >= from
                        && t.TransactionDate <= to
                        && (t.Status == TransactionStatus.Posted || t.Status == TransactionStatus.Pending))
            .Select(t => new { t.Id, t.Amount, t.Direction, t.TransactionDate, t.CardMovementType, t.InternalTransferLinkId })
            .ToListAsync(cancellationToken);

        if (!account.IsLiability && legs.Count > 0)
        {
            // On a bank account only the bank half of a CARD payment qualifies.
            var linkedIds = legs.Where(l => l.InternalTransferLinkId is not null).Select(l => l.InternalTransferLinkId!.Value).ToArray();
            var cardPayments = linkedIds.Length == 0
                ? new HashSet<Guid>()
                : (await db.Transactions
                    .AsNoTracking()
                    .Where(t => t.UserId == userId && t.CardMovementType == CreditCardMovementType.Payment)
                    .WhereIdIn(t => t.Id, linkedIds)
                    .Select(t => t.Id)
                    .ToListAsync(cancellationToken)).ToHashSet();
            legs = legs.Where(l => l.InternalTransferLinkId is { } link && cardPayments.Contains(link)).ToList();
        }
        else
        {
            legs = legs.Where(l => l.CardMovementType == CreditCardMovementType.Payment).ToList();
        }

        var claimed = matches.Where(m => m.Match is not null).Select(m => m.Match!.TransactionId).ToHashSet();
        foreach (var i in candidates)
        {
            var movement = movements[i];
            var leg = legs
                .Where(l => !claimed.Contains(l.Id)
                            && l.Direction == movement.Direction
                            && l.Amount == movement.Amount
                            && (l.TransactionDate - movement.TransactionDate).Duration() <= window)
                .OrderBy(l => (l.TransactionDate - movement.TransactionDate).Duration())
                .FirstOrDefault();

            if (leg is null)
            {
                continue;
            }

            claimed.Add(leg.Id);
            matches[i] = new DuplicateCheckResult(
                DuplicateMatchType.ProbableMatch,
                new DuplicateMatch(leg.Id, DuplicateMatchType.ProbableMatch, 0.9d, "card_payment_already_registered"));
        }
    }

    /// <summary>
    /// Tarjetas de crédito: statement lines like "LAPTOP CUOTA 4/12 $100" whose purchase
    /// is already in Fino as ONE $1,200 movement with an installment plan of the same
    /// number of installments and installment amount. Those lines are the plan's
    /// installments, not new purchases.
    /// </summary>
    private async Task<Dictionary<int, (Guid PurchaseId, string Reason)>> FindInstallmentLinesCoveredByPlansAsync(
        Guid userId,
        FinancialAccount account,
        IReadOnlyList<IncomingMovement> movements,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, (Guid, string)>();
        var markers = Enumerable.Range(0, movements.Count)
            .Where(i => movements[i].Direction == TransactionDirection.Expense)
            .Select(i => (Index: i, Marker: CreditCardMovementRules.ParseInstallmentMarker(movements[i].Description)))
            .Where(x => x.Marker is not null)
            .ToList();

        if (markers.Count == 0)
        {
            return result;
        }

        var card = await db.CreditCards.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.FinancialAccountId == account.Id, cancellationToken);
        if (card is null)
        {
            return result;
        }

        var plans = await db.InstallmentPlans
            .AsNoTracking()
            .Include(p => p.Installments)
            .Where(p => p.UserId == userId && p.CreditCardId == card.Id)
            .ToListAsync(cancellationToken);

        foreach (var (index, marker) in markers)
        {
            var amount = movements[index].Amount;
            var plan = plans.FirstOrDefault(p =>
                p.NumberOfInstallments == marker!.Value.Count
                && p.Installments.Any(i => i.Number == marker.Value.Number && Math.Abs(i.Amount - amount) <= 0.01m));
            if (plan is not null)
            {
                result[index] = (plan.TransactionId, $"Cuota {marker!.Value.Number}/{marker.Value.Count} de una compra diferida que ya está en Fino.");
            }
        }

        return result;
    }

    private async Task DeclareCardStatementAsync(Guid userId, FinancialAccount account, Import import, CancellationToken cancellationToken)
    {
        var card = await db.CreditCards
            .FirstOrDefaultAsync(c => c.UserId == userId && c.FinancialAccountId == account.Id, cancellationToken);
        if (card is null)
        {
            return;
        }

        var now = clock.UtcNow;
        if (import.CardCreditLimit is { } limit && limit != card.CreditLimit)
        {
            // The bank's statement is the authority on the limit (e.g. an increase).
            card.ChangeLimit(limit, now);
        }

        var closing = import.CardClosingDate!.Value;
        var due = import.CardDueDate!.Value;
        if (due <= closing)
        {
            return;
        }

        var minimum = import.CardMinimumPayment is { } m && m <= import.CardStatementBalance!.Value ? m : (decimal?)null;
        await creditCards.UpsertDeclaredStatementAsync(
            userId,
            card,
            closing,
            due,
            import.CardStatementBalance!.Value,
            minimum,
            import.CardPeriodStart,
            StatementSource.Imported,
            import.Id,
            cancellationToken);

        db.AuditLog.Add(AuditLogEntry.Record(userId, AuditActions.CreditCardStatementDeclared, "CreditCard", now, account.Id.ToString()));
        NexoTelemetry.CreditCardEvents.Add(1, new KeyValuePair<string, object?>("action", "statement_imported"));
    }

    private async Task<ImportPreviewDto> BuildPreviewAsync(
        Guid userId,
        Import import,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? unmappedColumns = null,
        IReadOnlyList<IReadOnlyList<string>>? unmappedSampleRows = null)
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
            r.Error,
            r.SkipReason)).ToList();

        return new ImportPreviewDto(
            import.Id,
            import.FinancialAccountId,
            import.FileName,
            import.ParserCode,
            import.DetectedProviderCode,
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
            rowDtos,
            unmappedColumns,
            unmappedSampleRows,
            import.HasCardStatementSummary || import.CardCreditLimit is not null || import.CardMinimumPayment is not null
                ? new CardStatementSummaryDto(
                    import.CardClosingDate,
                    import.CardDueDate,
                    import.CardStatementBalance,
                    import.CardMinimumPayment,
                    import.CardCreditLimit)
                : null);
    }

    internal static TabularTable ReadTable(byte[] bytes, string extension, int maxRows, int maxColumns = CsvTableReader.DefaultMaxColumns)
    {
        using var stream = new MemoryStream(bytes, writable: false);

        if (extension == ".xlsx" || XlsxTableReader.LooksLikeXlsx(bytes.AsSpan(0, Math.Min(4, bytes.Length))))
        {
            return XlsxTableReader.Read(stream, maxRows);
        }

        // Detección por contenido, no por extensión: Banco Guayaquil exporta un
        // documento HTML real con extensión ".xls" desde su banca personal (ver
        // HtmlTableReader). Un CSV nunca empieza así, así que revisar esto antes
        // de caer al lector de CSV es seguro para todos los demás bancos.
        if (HtmlTableReader.LooksLikeHtml(bytes.AsSpan(0, Math.Min(1024, bytes.Length))))
        {
            return HtmlTableReader.Read(stream, maxRows, maxColumns);
        }

        if (LooksLikeBinaryGarbage(bytes))
        {
            throw new InvalidDataException(
                "El archivo contiene datos binarios; no parece un CSV, XLSX o estado de cuenta HTML válido.");
        }

        return CsvTableReader.Read(stream, maxRows, maxColumns);
    }

    /// <summary>
    /// A renamed image, PDF, or executable dressed up with a ".csv" extension is
    /// not a formatting problem for the CSV reader to muddle through -- every
    /// text encoding it falls back to (UTF-8, then Latin-1) can represent any
    /// byte sequence, so it would "succeed" at producing garbage rows instead of
    /// failing cleanly. A NUL byte never appears in a real text file exported by
    /// a bank; its presence in the first few KB is the cheapest reliable signal
    /// that these bytes were never meant to be read as text at all.
    /// </summary>
    private static bool LooksLikeBinaryGarbage(byte[] bytes)
    {
        var sample = bytes.AsSpan(0, Math.Min(bytes.Length, 8000));
        return sample.Contains((byte)0);
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
