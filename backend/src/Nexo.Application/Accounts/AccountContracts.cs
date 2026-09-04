namespace Nexo.Application.Accounts;

public sealed record AccountDto(
    Guid Id,
    string ProviderCode,
    string ProviderName,
    string BrandColor,
    string? LogoKey,
    string Alias,
    string AccountType,
    string? Mask,
    string Currency,
    string ConnectionMode,
    decimal Balance,
    string BalanceType,
    decimal? LastVerifiedBalance,
    DateTimeOffset? LastVerifiedAt,
    DateTimeOffset? LastTransactionAt,
    DateTimeOffset? LastSyncedAt,
    bool IsArchived);

public sealed record CreateAccountRequest(
    string ProviderCode,
    string Alias,
    string AccountType,
    string ConnectionMode,
    string? Mask,
    decimal? OpeningVerifiedBalance,
    string? Currency);

public sealed record UpdateAccountRequest(string? Alias);

public sealed record SetVerifiedBalanceRequest(decimal Balance, DateTimeOffset? AsOf);
