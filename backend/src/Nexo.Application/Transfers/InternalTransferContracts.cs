namespace Nexo.Application.Transfers;

/// <summary>
/// One suggested pair: an outflow from one of the person's accounts and an inflow
/// into another, close enough in time and identical in amount that it is probably
/// the same money moving between their own accounts rather than real income or
/// spending.
/// </summary>
public sealed record InternalTransferCandidateDto(
    Guid OutgoingTransactionId,
    Guid OutgoingAccountId,
    string OutgoingAccountAlias,
    DateTimeOffset OutgoingDate,
    string OutgoingDescription,
    Guid IncomingTransactionId,
    Guid IncomingAccountId,
    string IncomingAccountAlias,
    DateTimeOffset IncomingDate,
    string IncomingDescription,
    decimal Amount,
    string Currency);

/// <summary>The person confirming (or manually pairing) two movements as one transfer.</summary>
public sealed record ConfirmInternalTransferRequest(Guid OutgoingTransactionId, Guid IncomingTransactionId);
