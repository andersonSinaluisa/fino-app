using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Application.CreditCards;
using Nexo.Domain.Common;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Transfers;

public interface IInternalTransferService
{
    /// <summary>Movements that look like the same money crossing between two of the person's own accounts.</summary>
    Task<IReadOnlyList<InternalTransferCandidateDto>> ListCandidatesAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Marks both legs as one confirmed transfer.</summary>
    Task ConfirmAsync(Guid userId, ConfirmInternalTransferRequest request, CancellationToken cancellationToken);

    /// <summary>Un-marks this movement and its paired leg -- e.g. it was matched by mistake.</summary>
    Task ClearAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken);
}

/// <summary>
/// Entregable 13: a transfer between the person's own accounts should never look
/// like income or spending. Detection is a plain in-memory pairing -- a person has
/// at most a handful of accounts and a bounded window of recent movements, never
/// enough to need anything cleverer than "an expense matched to an income of the
/// exact same amount, on a different account, a few days apart".
/// </summary>
public sealed class InternalTransferService(INexoDbContext db, IClock clock, ICardMovementClassifier cardMovements) : IInternalTransferService
{
    /// <summary>How far back a candidate pair is even considered.</summary>
    private const int LookbackDays = 180;

    /// <summary>"Ventana de tiempo cercana": how many days apart the two legs may post.</summary>
    private const int WindowDays = 3;

    public async Task<IReadOnlyList<InternalTransferCandidateDto>> ListCandidatesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var since = clock.UtcNow.AddDays(-LookbackDays);

        var movements = await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        // Tarjetas de crédito: a card payment imported from the card's
                        // statement is already neutral but has no bank leg yet -- it is
                        // exactly the incoming half this pairing looks for.
                        && (!t.IsInternalTransfer
                            || (t.InternalTransferLinkId == null && t.CardMovementType == CreditCardMovementType.Payment))
                        && t.TransactionDate >= since
                        && (t.Status == TransactionStatus.Posted || t.Status == TransactionStatus.Pending))
            .OrderBy(t => t.TransactionDate)
            .ToListAsync(cancellationToken);

        if (movements.Count < 2)
        {
            return [];
        }

        var aliases = await db.FinancialAccounts
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => new { a.Id, a.Alias })
            .ToDictionaryAsync(a => a.Id, a => a.Alias, cancellationToken);

        var outgoing = movements.Where(t => t.Direction == TransactionDirection.Expense && !t.IsInternalTransfer);
        var incoming = movements.Where(t => t.Direction == TransactionDirection.Income).ToList();

        // Greedy pairing, oldest first: once an incoming leg is claimed by one
        // expense it cannot also explain a second one. Good enough for a person's
        // own handful of transfers; two unrelated coincidental matches on the same
        // day are rare and the person can simply ignore a wrong suggestion.
        var claimedIncoming = new HashSet<Guid>();
        var candidates = new List<InternalTransferCandidateDto>();

        foreach (var expense in outgoing)
        {
            var match = incoming
                .Where(income => !claimedIncoming.Contains(income.Id)
                                  && income.FinancialAccountId != expense.FinancialAccountId
                                  && income.Amount == expense.Amount
                                  && income.Currency == expense.Currency
                                  && Math.Abs((income.TransactionDate - expense.TransactionDate).TotalDays) <= WindowDays)
                .OrderBy(income => Math.Abs((income.TransactionDate - expense.TransactionDate).TotalDays))
                .FirstOrDefault();

            if (match is null)
            {
                continue;
            }

            claimedIncoming.Add(match.Id);

            candidates.Add(new InternalTransferCandidateDto(
                expense.Id,
                expense.FinancialAccountId,
                aliases.GetValueOrDefault(expense.FinancialAccountId, "Cuenta"),
                expense.TransactionDate,
                expense.Description,
                match.Id,
                match.FinancialAccountId,
                aliases.GetValueOrDefault(match.FinancialAccountId, "Cuenta"),
                match.TransactionDate,
                match.Description,
                expense.Amount,
                expense.Currency));
        }

        return candidates;
    }

    public async Task ConfirmAsync(Guid userId, ConfirmInternalTransferRequest request, CancellationToken cancellationToken)
    {
        if (request.OutgoingTransactionId == request.IncomingTransactionId)
        {
            throw new DomainException("invalid_transfer_pair", "A transfer needs two different movements.");
        }

        var outgoing = await RequireTransactionAsync(userId, request.OutgoingTransactionId, cancellationToken);
        var incoming = await RequireTransactionAsync(userId, request.IncomingTransactionId, cancellationToken);

        if (outgoing.FinancialAccountId == incoming.FinancialAccountId)
        {
            throw new DomainException("same_account", "A transfer must move money between two different accounts.");
        }

        if (outgoing.Direction != TransactionDirection.Expense || incoming.Direction != TransactionDirection.Income)
        {
            throw new DomainException(
                "invalid_direction",
                "The first movement must be the outgoing expense and the second the incoming deposit.");
        }

        if (outgoing.Amount != incoming.Amount || outgoing.Currency != incoming.Currency)
        {
            throw new DomainException("amount_mismatch", "Both legs of a transfer must be the exact same amount and currency.");
        }

        if (outgoing.InternalTransferLinkId is not null || incoming.InternalTransferLinkId is not null)
        {
            throw new ConflictException("Uno de los dos movimientos ya está vinculado con otra cuenta.");
        }

        var now = clock.UtcNow;

        // Tarjetas de crédito: money arriving at a card is a payment to it; money
        // leaving a card for one of your accounts is a cash advance. Typed here so the
        // card's statements and next payment see them for what they are.
        var outgoingAccount = await db.FinancialAccounts.FirstAsync(a => a.Id == outgoing.FinancialAccountId && a.UserId == userId, cancellationToken);
        var incomingAccount = await db.FinancialAccounts.FirstAsync(a => a.Id == incoming.FinancialAccountId && a.UserId == userId, cancellationToken);
        if (incomingAccount.IsLiability)
        {
            await cardMovements.ApplyAsync(incomingAccount, incoming, CreditCardMovementType.Payment, now, cancellationToken);
        }

        if (outgoingAccount.IsLiability)
        {
            await cardMovements.ApplyAsync(outgoingAccount, outgoing, CreditCardMovementType.CashAdvance, now, cancellationToken);
        }

        outgoing.MarkAsInternalTransfer(incoming.Id, now);
        incoming.MarkAsInternalTransfer(outgoing.Id, now);

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken)
    {
        var transaction = await RequireTransactionAsync(userId, transactionId, cancellationToken);
        var now = clock.UtcNow;

        if (transaction.InternalTransferLinkId is { } linkedId)
        {
            var linked = await db.Transactions
                .FirstOrDefaultAsync(t => t.Id == linkedId && t.UserId == userId, cancellationToken);
            linked?.ClearInternalTransfer(now);
        }

        transaction.ClearInternalTransfer(now);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Transaction> RequireTransactionAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken) =>
        await db.Transactions.FirstOrDefaultAsync(t => t.Id == transactionId && t.UserId == userId, cancellationToken)
        ?? throw new NotFoundException("Transaction", transactionId);
}
