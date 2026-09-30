using Microsoft.EntityFrameworkCore;
using Nexo.Application.Abstractions;
using Nexo.Application.Common;
using Nexo.Application.Insights;
using Nexo.Domain.Categories;
using Nexo.Domain.Common;
using Nexo.Domain.Transactions;

namespace Nexo.Application.Transactions;

public interface ITransactionSplitService
{
    Task<TransactionSplitsDto> GetAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken);

    /// <summary>Replaces the whole division in one database transaction.</summary>
    Task<TransactionDetailDto> ReplaceAsync(Guid userId, Guid transactionId, ReplaceSplitsRequest request, CancellationToken cancellationToken);

    /// <summary>"Quitar división": the movement goes back to one category (or none).</summary>
    Task<TransactionDetailDto> RemoveAsync(Guid userId, Guid transactionId, Guid? categoryId, int? expectedVersion, CancellationToken cancellationToken);
}

/// <summary>
/// Movimientos divididos. The bank movement is never modified here -- amount, date,
/// description, reference, account, import, fingerprint and status stay exactly as
/// they were; only the analytic distribution (splits) changes. So Tu dinero,
/// Comprometido and Disponible never move because of a division: balances are
/// computed from Transaction.Amount, which a division cannot touch.
/// </summary>
public sealed class TransactionSplitService(
    INexoDbContext db,
    IClock clock,
    TransactionService transactions,
    IInsightEngine insights) : ITransactionSplitService
{
    public async Task<TransactionSplitsDto> GetAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken)
    {
        var transaction = await db.Transactions
                              .AsNoTracking()
                              .Include(t => t.Splits)
                              .FirstOrDefaultAsync(t => t.Id == transactionId && t.UserId == userId, cancellationToken)
                          ?? throw new NotFoundException("Transaction", transactionId);

        var categories = await LoadCategoriesAsync(userId, cancellationToken);

        return new TransactionSplitsDto(
            transaction.Id,
            transaction.Amount,
            transaction.Direction.ToString(),
            transaction.IsSplit,
            transaction.SplitVersion,
            TransactionService.MapSplits(transaction.Splits, categories));
    }

    public async Task<TransactionDetailDto> ReplaceAsync(
        Guid userId,
        Guid transactionId,
        ReplaceSplitsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Splits is null)
        {
            throw ValidationException.For("Splits", "Envía la división completa.");
        }

        var transaction = await LoadForUpdateAsync(userId, transactionId, request.ExpectedVersion, cancellationToken);
        var categories = await LoadCategoriesAsync(userId, cancellationToken);

        foreach (var line in request.Splits)
        {
            if (line.CategoryId is { } id && !categories.ContainsKey(id))
            {
                throw new NotFoundException("Category", id);
            }
        }

        try
        {
            transaction.Split(
                request.Splits.Select(l => new SplitLine(l.CategoryId, l.Amount, l.Note)).ToList(),
                clock.UtcNow);
        }
        catch (DomainException exception)
        {
            throw ValidationException.For("Splits", exception.Message);
        }

        await SaveAsync(cancellationToken);
        await insights.RecomputeAsync(userId, cancellationToken);

        return await transactions.MapDetailAsync(userId, transaction, cancellationToken);
    }

    public async Task<TransactionDetailDto> RemoveAsync(
        Guid userId,
        Guid transactionId,
        Guid? categoryId,
        int? expectedVersion,
        CancellationToken cancellationToken)
    {
        var transaction = await LoadForUpdateAsync(userId, transactionId, expectedVersion, cancellationToken);

        if (categoryId is { } id && !(await LoadCategoriesAsync(userId, cancellationToken)).ContainsKey(id))
        {
            throw new NotFoundException("Category", id);
        }

        if (!transaction.IsSplit)
        {
            throw new ConflictException("Este movimiento no está dividido.");
        }

        transaction.RemoveSplit(categoryId, clock.UtcNow);

        await SaveAsync(cancellationToken);
        await insights.RecomputeAsync(userId, cancellationToken);

        return await transactions.MapDetailAsync(userId, transaction, cancellationToken);
    }

    private async Task<Transaction> LoadForUpdateAsync(
        Guid userId,
        Guid transactionId,
        int? expectedVersion,
        CancellationToken cancellationToken)
    {
        var transaction = await db.Transactions
                              .Include(t => t.Splits)
                              .FirstOrDefaultAsync(t => t.Id == transactionId && t.UserId == userId, cancellationToken)
                          ?? throw new NotFoundException("Transaction", transactionId);

        if (expectedVersion is { } version && version != transaction.SplitVersion)
        {
            throw new ConflictException("La división cambió mientras la editabas. Vuelve a abrirla para ver la versión actual.");
        }

        return transaction;
    }

    /// <summary>
    /// One SaveChanges = one database transaction: the old parts are deleted and the
    /// new ones inserted together, or nothing happens. Transaction.SplitVersion is a
    /// concurrency token, so if another request changed the division in between,
    /// this UPDATE matches zero rows and the whole save is rolled back.
    /// </summary>
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("La división cambió mientras la editabas. Vuelve a abrirla para ver la versión actual.");
        }
    }

    private async Task<Dictionary<Guid, Category>> LoadCategoriesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var categories = await db.Categories
            .AsNoTracking()
            .Where(c => c.UserId == null || c.UserId == userId)
            .ToListAsync(cancellationToken);
        return categories.ToDictionary(c => c.Id);
    }
}
