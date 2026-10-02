using Nexo.Application.Transactions;

namespace Nexo.Application.CreditCards;

/// <summary>
/// Tarjetas de crédito. A card is addressed everywhere by its ACCOUNT id
/// (<see cref="CreditCardSummaryDto.Id"/>): it is the same id its movements,
/// imports and filters already use. Every amount below comes from
/// <c>CreditCardCalculator</c>; the client only presents them.
/// </summary>
/// <param name="NeedsSetup">A card account created before this module, with no limit/corte/pago yet.</param>
/// <param name="CurrentDebt">Deuda actual (deferred installments included).</param>
/// <param name="CreditBalance">Saldo a favor, if the card was overpaid.</param>
/// <param name="AvailableCredit">Cupo disponible. Credit, not money: never part of Tu dinero or Disponible.</param>
/// <param name="DeferredDebt">Debt in installments of later statements (deuda futura).</param>
/// <param name="CommittedContribution">What the card adds to Comprometido right now.</param>
public sealed record CreditCardSummaryDto(
    Guid Id,
    string Name,
    string ProviderCode,
    string ProviderName,
    string BrandColor,
    string? LastFour,
    string Currency,
    string? Network,
    bool IsArchived,
    bool NeedsSetup,
    decimal? CreditLimit,
    int? ClosingDay,
    int? PaymentDueDay,
    bool AutoReserve,
    decimal CurrentDebt,
    decimal CreditBalance,
    decimal? AvailableCredit,
    decimal? UtilizationPercent,
    bool IsOverLimit,
    decimal DeferredDebt,
    CreditCardNextPaymentDto? NextPayment,
    decimal CommittedContribution,
    string BalanceType,
    DateTimeOffset? LastSyncedAt);

/// <param name="Source">"statement" (pending part of a closed statement) or "current_cycle" (projection of the open one).</param>
public sealed record CreditCardNextPaymentDto(
    decimal Amount,
    DateOnly DueDate,
    decimal? MinimumPayment,
    string Source,
    bool IsOverdue,
    int DaysUntilDue);

/// <param name="TotalDebt">Sum of every active card's debt. A liability: never subtracted from Tu dinero here.</param>
/// <param name="TotalNextPayments">Sum of every active card's next payment.</param>
/// <param name="TotalCommitted">Part of TotalNextPayments that feeds Comprometido (auto-reserve on).</param>
public sealed record CreditCardListDto(
    IReadOnlyList<CreditCardSummaryDto> Cards,
    decimal TotalDebt,
    decimal TotalNextPayments,
    decimal TotalCommitted,
    string Currency);

public sealed record CreditCardCycleDto(
    DateOnly Start,
    DateOnly Closing,
    DateOnly Due,
    decimal ProjectedBalance,
    decimal Charges,
    decimal Credits);

/// <param name="Status">"Open" | "Closed" | "PartiallyPaid" | "Paid" | "Overdue".</param>
/// <param name="IsDeclared">True when Total/Mínimo come from the bank (imported or typed), false when computed from movements.</param>
public sealed record CreditCardStatementDto(
    DateOnly PeriodStart,
    DateOnly ClosingDate,
    DateOnly DueDate,
    decimal StatementBalance,
    decimal? MinimumPayment,
    decimal AmountPaid,
    decimal Pending,
    string Status,
    bool IsDeclared,
    string? DeclaredSource,
    decimal Charges,
    decimal Credits,
    bool IsCurrent);

/// <summary>"Este mes" on the card's home, by movement type.</summary>
public sealed record CreditCardActivityDto(
    string Label,
    decimal Purchases,
    decimal Refunds,
    decimal Payments,
    decimal Interest,
    decimal Fees,
    decimal CashAdvances,
    decimal Adjustments);

/// <param name="Status">"Upcoming" | "Billed".</param>
public sealed record InstallmentDto(int Number, decimal Amount, DateOnly ClosingDate, DateOnly DueDate, string Status);

/// <param name="BilledInstallments">How many installments have been billed ("4" in "4/12").</param>
/// <param name="OutstandingAmount">What is still to be billed (deuda futura of this purchase).</param>
/// <param name="Status">"Active" | "Completed" | "Cancelled".</param>
public sealed record InstallmentPlanDto(
    Guid Id,
    Guid TransactionId,
    string Description,
    string? CategoryName,
    DateOnly PurchaseDate,
    decimal OriginalAmount,
    int NumberOfInstallments,
    decimal InstallmentAmount,
    decimal? InterestRate,
    int BilledInstallments,
    int? NextInstallmentNumber,
    decimal? NextInstallmentAmount,
    DateOnly? NextInstallmentClosingDate,
    DateOnly? NextInstallmentDueDate,
    decimal OutstandingAmount,
    string Status,
    IReadOnlyList<InstallmentDto> Installments);

public sealed record CreditCardDetailDto(
    CreditCardSummaryDto Card,
    CreditCardCycleDto? CurrentCycle,
    CreditCardStatementDto? LastStatement,
    CreditCardActivityDto ThisMonth,
    IReadOnlyList<InstallmentPlanDto> ActiveInstallments,
    IReadOnlyList<TransactionListItemDto> RecentMovements);

/// <summary>
/// POST /credit-cards. Deliberately no full card number, CVV, PIN or bank
/// credential: <paramref name="LastFour"/> keeps at most the last four digits.
/// </summary>
/// <param name="CurrentDebt">Optional "Deuda actual" today, as the bank shows it (a positive number).</param>
public sealed record CreateCreditCardRequest(
    string Name,
    string ProviderCode,
    string? LastFour,
    decimal CreditLimit,
    int ClosingDay,
    int PaymentDueDay,
    bool AutoReserve = true,
    string? Network = null,
    string? Currency = null,
    decimal? CurrentDebt = null);

/// <summary>PUT /credit-cards/{id}. Also completes the setup of a card account created before this module.</summary>
public sealed record UpdateCreditCardRequest(
    string? Name,
    string? LastFour,
    decimal CreditLimit,
    int ClosingDay,
    int PaymentDueDay,
    bool AutoReserve,
    string? Network = null);

/// <summary>"Actualizar deuda": what the bank says is owed today (positive), becoming the card's verified balance.</summary>
public sealed record SetCreditCardDebtRequest(decimal CurrentDebt, DateTimeOffset? AsOf = null);

/// <summary>PUT /credit-cards/{id}/statements: the official figures of one statement.</summary>
public sealed record DeclareStatementRequest(
    DateOnly ClosingDate,
    DateOnly DueDate,
    decimal StatementBalance,
    decimal? MinimumPayment = null,
    DateOnly? PeriodStart = null);

/// <summary>POST /credit-cards/{id}/installments: defer one card purchase.</summary>
/// <param name="FirstClosingDate">Statement that bills installment 1. Default: the one containing the purchase.</param>
public sealed record CreateInstallmentPlanRequest(
    Guid TransactionId,
    int NumberOfInstallments,
    decimal? InterestRate = null,
    DateOnly? FirstClosingDate = null);

/// <summary>
/// POST /credit-cards/{id}/payments: "Pagar tarjeta". With <paramref name="SourceAccountId"/>
/// it records ONE payment as two linked legs (bank −, card +) that are never income or
/// spending; without it (paid from an account not in Fino) only the card leg.
/// </summary>
public sealed record RegisterCardPaymentRequest(
    decimal Amount,
    Guid? SourceAccountId = null,
    DateTimeOffset? PaidAt = null,
    Guid? ClientRequestId = null,
    string? Note = null);

/// <summary>
/// POST /credit-cards/{id}/payments/link: a bank movement already in Fino IS a
/// payment to this card. With <paramref name="CardTransactionId"/> it is paired with
/// that existing card payment; without it the card leg is created.
/// </summary>
public sealed record LinkCardPaymentRequest(Guid BankTransactionId, Guid? CardTransactionId = null);

/// <param name="Reason">"matches_card_payment" (same amount as an unpaired payment on the card) or "looks_like_card_payment" (the bank's text says so).</param>
public sealed record CardPaymentSuggestionDto(
    Guid BankTransactionId,
    Guid BankAccountId,
    string BankAccountAlias,
    DateTimeOffset Date,
    decimal Amount,
    string Description,
    Guid? CardTransactionId,
    string Reason);

/// <summary>The two legs of one card payment.</summary>
public sealed record CardPaymentResultDto(
    Guid CardTransactionId,
    Guid? BankTransactionId,
    CreditCardSummaryDto Card);

/// <summary>POST /credit-cards/{id}/movements/{transactionId}/type: reclassify a card movement.</summary>
public sealed record ReclassifyCardMovementRequest(string Type);
