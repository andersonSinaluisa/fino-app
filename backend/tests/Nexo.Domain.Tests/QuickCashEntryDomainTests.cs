using Nexo.Domain.Accounts;
using Nexo.Domain.Common;
using Nexo.Domain.Providers;
using Nexo.Domain.Transactions;
using Xunit;

namespace Nexo.Domain.Tests;

/// <summary>
/// Registro rápido de efectivo, al nivel del dominio: las reglas que ningún
/// servicio puede saltarse porque viven dentro de la entidad. La pregunta que
/// contestan estas pruebas es si el efectivo cabe de verdad en el modelo que ya
/// existía, sin un tipo nuevo ni una excepción al lado.
/// </summary>
public class QuickCashEntryDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 17, 0, 0, TimeSpan.Zero);

    private static Provider CashProvider() => Provider.Create(
        ProviderCodes.Cash,
        "Efectivo",
        "Efectivo",
        ProviderKind.Wallet,
        [ConnectionMode.Manual],
        Now);

    private static Transaction ManualExpense(decimal amount = 5m, string description = "Almuerzo") =>
        Transaction.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            ProviderCodes.Cash,
            Now,
            amount,
            TransactionDirection.Expense,
            description,
            TransactionSource.Manual,
            Now);

    [Fact]
    public void A_cash_account_can_only_be_opened_in_manual_mode()
    {
        var provider = CashProvider();

        // El proveedor de efectivo declara UN solo modo, y esa declaración es la que
        // impide que el efectivo aparezca en los flujos de importar archivo o
        // conectar correo -- no un `if` repartido por la interfaz.
        Assert.True(provider.Supports(ConnectionMode.Manual));
        Assert.False(provider.Supports(ConnectionMode.ManualImport));
        Assert.False(provider.HasAutomaticConnection);

        Assert.Throws<DomainException>(() => FinancialAccount.Open(
            Guid.CreateVersion7(),
            provider,
            "Efectivo",
            AccountType.Cash,
            ConnectionMode.ManualImport,
            Now));
    }

    [Fact]
    public void A_handwritten_movement_moves_the_balance_exactly_like_an_imported_one()
    {
        var account = FinancialAccount.Open(
            Guid.CreateVersion7(),
            CashProvider(),
            "Efectivo",
            AccountType.Cash,
            ConnectionMode.Manual,
            Now,
            openingVerifiedBalance: 50m);

        var expense = ManualExpense(5m);
        account.ApplyMovement(expense.SignedAmount, Now.AddMinutes(1), Now);

        Assert.Equal(45m, account.EstimatedBalance);

        // Y deja de estar "verificado" en cuanto se mueve: el saldo de efectivo es
        // una estimación desde el momento en que hay un movimiento posterior a la
        // última vez que la persona contó lo que tenía.
        Assert.Equal(BalanceType.Estimated, account.BalanceKind);
    }

    [Fact]
    public void A_handwritten_movement_can_be_corrected_and_its_fingerprint_follows()
    {
        var transaction = ManualExpense(5m, "Almerzo");
        var originalFingerprint = transaction.Fingerprint;

        transaction.UpdateManualDetails(
            8m,
            TransactionDirection.Income,
            "Me pagaron",
            Now.AddDays(-1),
            Now);

        Assert.Equal(8m, transaction.Amount);
        Assert.Equal(TransactionDirection.Income, transaction.Direction);
        Assert.Equal(8m, transaction.SignedAmount);
        Assert.Equal("Me pagaron", transaction.Description);

        // La huella es lo que usa la deduplicación para decidir si dos movimientos
        // son el mismo. Si no se recalculara al editar, el movimiento seguiría
        // "siendo" el que era y podría chocar con una importación posterior.
        Assert.NotEqual(originalFingerprint, transaction.Fingerprint);
        Assert.Equal(
            TransactionFingerprint.Compute(
                transaction.ProviderCode,
                transaction.FinancialAccountId,
                transaction.ExternalReference,
                transaction.TransactionDate,
                transaction.Amount,
                transaction.Direction,
                transaction.Description),
            transaction.Fingerprint);
    }

    [Fact]
    public void A_bank_movement_refuses_to_be_rewritten()
    {
        var imported = Transaction.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            ProviderCodes.Pichincha,
            Now,
            48.20m,
            TransactionDirection.Expense,
            "SUPERMAXI ALBORADA",
            TransactionSource.Import,
            Now);

        // Lo que dice un extracto es un hecho reportado por el banco. Corregir el
        // comercio o la categoría sí se puede (y no toca el original); reescribir el
        // importe o la fecha, no.
        var error = Assert.Throws<DomainException>(() =>
            imported.UpdateManualDetails(1m, TransactionDirection.Expense, "Otra cosa", Now, Now));

        Assert.Equal("not_manual", error.Code);
    }

    [Fact]
    public void Clearing_the_category_by_hand_is_remembered_as_a_decision()
    {
        var transaction = ManualExpense();
        var categoryId = Guid.CreateVersion7();

        transaction.ApplyAutomaticCategory(categoryId, Now);
        Assert.Equal(categoryId, transaction.CategoryId);

        transaction.ClearCategoryManually(Now);

        Assert.Null(transaction.CategoryId);
        Assert.Equal(CategorySource.Uncategorized, transaction.CategorySource);

        // La diferencia que importa: "nadie lo ha mirado todavía" y "la persona lo
        // dejó sin categoría a propósito" se ven igual en CategoryId, pero solo el
        // segundo impide que una regla vuelva a categorizarlo.
        Assert.True(transaction.CategoryManuallySet);
        Assert.False(transaction.ApplyAutomaticCategory(Guid.CreateVersion7(), Now));
        Assert.Null(transaction.CategoryId);
    }

    [Fact]
    public void A_zero_amount_is_rejected_however_it_arrives()
    {
        Assert.Throws<DomainException>(() => ManualExpense(0m));

        var transaction = ManualExpense(5m);
        Assert.Throws<DomainException>(() =>
            transaction.UpdateManualDetails(0m, TransactionDirection.Expense, "Nada", Now, Now));
    }

    [Fact]
    public void Reusing_an_archived_cash_account_reopens_it_instead_of_splitting_the_balance()
    {
        var account = FinancialAccount.Open(
            Guid.CreateVersion7(),
            CashProvider(),
            "Efectivo",
            AccountType.Cash,
            ConnectionMode.Manual,
            Now,
            openingVerifiedBalance: 20m);

        account.Archive(Now);
        Assert.True(account.IsArchived);

        account.Unarchive(Now);

        // Archivar nunca borró nada, así que reabrir no reconstruye: el saldo y el
        // historial siguen donde estaban. Crear una segunda cuenta "Efectivo" en su
        // lugar partiría el dinero de la persona en dos.
        Assert.False(account.IsArchived);
        Assert.Equal(20m, account.EstimatedBalance);
    }

    [Fact]
    public void The_client_request_id_travels_with_the_movement()
    {
        var clientRequestId = Guid.CreateVersion7();

        var transaction = Transaction.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            ProviderCodes.Cash,
            Now,
            5m,
            TransactionDirection.Expense,
            "Almuerzo",
            TransactionSource.Manual,
            Now,
            clientRequestId: clientRequestId);

        Assert.Equal(clientRequestId, transaction.ClientRequestId);

        // Y sigue siendo nulo para todo lo que no venga de un registro manual: las
        // importaciones y los correos ya se desduplican por huella, y ocupar el
        // índice único con ellos no aportaría nada.
        Assert.Null(ManualExpense().ClientRequestId);
    }
}
