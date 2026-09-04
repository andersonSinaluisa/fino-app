using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Application.Abstractions;
using Nexo.Application.Categorization;
using Nexo.Application.Deduplication;
using Nexo.Domain.Accounts;
using Nexo.Domain.Common;
using Nexo.Domain.Notifications;
using Nexo.Domain.Transactions;

namespace Nexo.Application.EmailIngestion;

public interface IEmailIngestionPipeline
{
    Task<EmailIngestionResult> ProcessAsync(
        Guid userId,
        Guid? emailConnectionId,
        EmailMessage message,
        CancellationToken cancellationToken);
}

/// <summary>
/// EmailIngestion -> SenderValidation -> ProviderDetector -> EmailParser ->
/// TransactionNormalizer -> Deduplication -> Categorization -> Transaction.
///
/// Each stage can reject; nothing downstream runs on an untrusted message. The
/// original email body is never persisted: only the fields needed to describe the
/// movement survive this method (data minimisation, see docs/security.md).
/// </summary>
public sealed class EmailIngestionPipeline(
    INexoDbContext db,
    ISenderValidator senderValidator,
    IBankEmailParserResolver parserResolver,
    IDeduplicationService deduplication,
    ICategorizationEngine categorization,
    IRealtimeNotifier realtime,
    INotificationDispatcher notifications,
    IClock clock,
    ILogger<EmailIngestionPipeline> logger) : IEmailIngestionPipeline
{
    public async Task<EmailIngestionResult> ProcessAsync(
        Guid userId,
        Guid? emailConnectionId,
        EmailMessage message,
        CancellationToken cancellationToken)
    {
        var validation = await senderValidator.ValidateAsync(message, cancellationToken);
        if (!validation.IsAccepted)
        {
            if (validation.Outcome == SenderValidationOutcome.FailedAuthentication)
            {
                // Looks like a bank, is not authenticated: this is what a phishing
                // attempt looks like, so the user gets told instead of ignored.
                logger.LogWarning(
                    "Rejected an unauthenticated message claiming to be {ProviderCode} for user {UserId}.",
                    validation.ProviderCode,
                    userId);

                await notifications.DispatchAsync(
                    Notification.Create(
                        userId,
                        NotificationType.SecurityAlert,
                        "Correo sospechoso ignorado",
                        "Recibimos un correo que aparentaba ser de tu banco pero no superó la verificación. No lo tomamos en cuenta.",
                        clock.UtcNow),
                    cancellationToken);
            }

            return new EmailIngestionResult(
                EmailIngestionOutcome.Rejected,
                Reason: validation.Outcome.ToString());
        }

        var parser = parserResolver.Resolve(message, validation.ProviderCode);
        if (parser is null)
        {
            return new EmailIngestionResult(
                EmailIngestionOutcome.NotAMovement,
                ProviderCode: validation.ProviderCode);
        }

        var parsed = await parser.ParseAsync(message, cancellationToken);
        if (parsed is null)
        {
            return new EmailIngestionResult(
                EmailIngestionOutcome.NotAMovement,
                ProviderCode: validation.ProviderCode);
        }

        var account = await ResolveAccountAsync(userId, parsed, cancellationToken);
        if (account is null)
        {
            return new EmailIngestionResult(
                EmailIngestionOutcome.NoMatchingAccount,
                ProviderCode: parsed.ProviderCode,
                Reason: "El usuario no tiene una cuenta registrada para este proveedor.");
        }

        var now = clock.UtcNow;
        var fingerprint = TransactionFingerprint.Compute(
            account.ProviderCode,
            account.Id,
            parsed.ExternalReference,
            parsed.OccurredAt,
            parsed.Amount,
            parsed.Direction,
            parsed.Description);

        var movement = new IncomingMovement(
            account.Id,
            account.ProviderCode,
            parsed.OccurredAt,
            parsed.Amount,
            parsed.Direction,
            parsed.Description,
            parsed.ExternalReference,
            fingerprint);

        var duplicate = await deduplication.CheckAsync(userId, movement, cancellationToken);
        if (duplicate.IsExact)
        {
            return new EmailIngestionResult(
                EmailIngestionOutcome.Duplicate,
                duplicate.Match?.TransactionId,
                parsed.ProviderCode);
        }

        var session = await categorization.StartSessionAsync(userId, cancellationToken);
        var suggestion = session.Suggest(movement.NormalizedDescription, movement.Direction);
        var categoryId = suggestion?.CategoryId ?? session.FallbackCategoryId(movement.Direction);

        var transaction = Transaction.Create(
            userId,
            account.Id,
            account.ProviderCode,
            parsed.OccurredAt,
            parsed.Amount,
            parsed.Direction,
            parsed.Description,
            TransactionSource.Email,
            now,
            account.Currency,
            parsed.ExternalReference,
            parsed.Merchant,
            categoryId == Guid.Empty ? null : categoryId,
            parsed.AccountMask ?? account.Mask,
            parsed.Confidence,
            // Email tells us something happened; the statement confirms it later.
            TransactionStatus.Pending,
            emailConnectionId: emailConnectionId);

        if (duplicate.IsProbable && duplicate.Match is not null)
        {
            transaction.FlagAsPossibleDuplicate(duplicate.Match.TransactionId, now);
        }

        db.Transactions.Add(transaction);

        // NeedsReview rows are excluded from the balance everywhere else, so adding
        // them here would double-count until the next recalculation.
        if (transaction.CountsTowardsBalance)
        {
            account.ApplyMovement(transaction.SignedAmount, transaction.TransactionDate, now);
        }

        account.MarkSynced(now);

        if (emailConnectionId is { } connectionId)
        {
            var connection = await db.EmailConnections
                .FirstOrDefaultAsync(c => c.Id == connectionId && c.UserId == userId, cancellationToken);
            connection?.RecordSync(null, 1, 1, now);
        }

        await db.SaveChangesAsync(cancellationToken);

        await notifications.DispatchAsync(
            Notification.Create(
                userId,
                NotificationType.TransactionDetected,
                parsed.Direction == TransactionDirection.Income ? "Ingreso detectado" : "Compra detectada",
                parsed.Direction == TransactionDirection.Income
                    ? $"Recibiste ${parsed.Amount:N2} en {account.Alias}."
                    : $"Gastaste ${parsed.Amount:N2} en {parsed.Merchant ?? account.Alias}.",
                now,
                payload: $"{{\"transactionId\":\"{transaction.Id}\"}}"),
            cancellationToken);

        await realtime.TransactionsChangedAsync(userId, 1, cancellationToken);

        return new EmailIngestionResult(EmailIngestionOutcome.Created, transaction.Id, parsed.ProviderCode);
    }

    /// <summary>
    /// Matches the message to one of the user's accounts: same provider, and when the
    /// notification exposes a card mask, the account carrying that mask wins.
    /// </summary>
    private async Task<FinancialAccount?> ResolveAccountAsync(
        Guid userId,
        ParsedTransaction parsed,
        CancellationToken cancellationToken)
    {
        var candidates = await db.FinancialAccounts
            .Where(a => a.UserId == userId && !a.IsArchived && a.ProviderCode == parsed.ProviderCode)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(parsed.AccountMask))
        {
            var byMask = candidates.FirstOrDefault(a =>
                string.Equals(a.Mask, parsed.AccountMask, StringComparison.Ordinal));
            if (byMask is not null)
            {
                return byMask;
            }
        }

        return candidates.Count == 1 ? candidates[0] : candidates.OrderBy(a => a.DisplayOrder).First();
    }
}
