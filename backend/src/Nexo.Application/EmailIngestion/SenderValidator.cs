using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;

namespace Nexo.Application.EmailIngestion;

/// <summary>
/// The first and most important gate of the email pipeline.
///
/// A display name saying "Banco Pichincha" costs nothing to forge, so it is never
/// consulted. What counts is the envelope sender matching a configured domain and
/// the message having passed authentication upstream. Everything else is discarded
/// before a single pattern runs.
/// </summary>
public sealed class SenderValidator(INexoDbContext db) : ISenderValidator
{
    public async Task<SenderValidationResult> ValidateAsync(
        EmailMessage message,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.EnvelopeFrom))
        {
            return new SenderValidationResult(SenderValidationOutcome.UnknownSender, null);
        }

        var senders = await db.TrustedSenders
            .AsNoTracking()
            .Where(s => s.IsActive)
            .ToListAsync(cancellationToken);

        var matched = senders.FirstOrDefault(s => s.Accepts(message.EnvelopeFrom, message.PassedAuthentication));
        if (matched is not null)
        {
            return new SenderValidationResult(SenderValidationOutcome.Accepted, matched.ProviderCode);
        }

        // Distinguish "we don't know you" from "you look like a bank but failed
        // authentication": the second one is a phishing signal worth alerting on.
        var domainMatch = senders.FirstOrDefault(s => s.Accepts(message.EnvelopeFrom, authenticated: true));

        return domainMatch is not null
            ? new SenderValidationResult(SenderValidationOutcome.FailedAuthentication, domainMatch.ProviderCode)
            : new SenderValidationResult(SenderValidationOutcome.UnknownSender, null);
    }
}
