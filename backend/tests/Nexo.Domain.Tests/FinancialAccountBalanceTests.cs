using Nexo.Domain.Accounts;
using Nexo.Domain.Common;
using Nexo.Domain.Providers;
using Xunit;

namespace Nexo.Domain.Tests;

public class FinancialAccountBalanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private static FinancialAccount NewAccount(decimal? opening = null)
    {
        var provider = Provider.Create(
            ProviderCodes.Pichincha, "Banco Pichincha", "Pichincha", ProviderKind.Bank,
            [ConnectionMode.ManualImport], Now);

        return FinancialAccount.Open(
            Guid.CreateVersion7(), provider, "Banco Pichincha", AccountType.Savings,
            ConnectionMode.ManualImport, Now, "4821", Currency.Usd, opening);
    }

    [Fact]
    public void A_freshly_imported_balance_is_verified()
    {
        var account = NewAccount(1000m);

        Assert.Equal(BalanceType.Verified, account.BalanceKind);
        Assert.Equal(1000m, account.EstimatedBalance);
    }

    [Fact]
    public void The_example_from_the_spec_produces_an_estimated_balance_of_1232()
    {
        var account = NewAccount(1000m);

        account.ApplyMovement(300m, Now.AddDays(1), Now);
        account.ApplyMovement(-48m, Now.AddDays(2), Now);
        account.ApplyMovement(-20m, Now.AddDays(3), Now);

        Assert.Equal(1232m, account.EstimatedBalance);
        Assert.Equal(BalanceType.Estimated, account.BalanceKind);
    }

    [Fact]
    public void Movements_dated_before_the_verified_anchor_are_already_inside_it()
    {
        var account = NewAccount();
        account.SetVerifiedBalance(500m, Now, Now);

        account.ApplyMovement(-100m, Now.AddDays(-5), Now);

        Assert.Equal(500m, account.EstimatedBalance);
        Assert.Equal(BalanceType.Verified, account.BalanceKind);
    }

    [Fact]
    public void Verifying_again_resets_the_estimate_to_what_the_bank_says()
    {
        var account = NewAccount(1000m);
        account.ApplyMovement(-250m, Now.AddDays(1), Now);
        Assert.Equal(750m, account.EstimatedBalance);

        account.SetVerifiedBalance(742.15m, Now.AddDays(2), Now);

        Assert.Equal(742.15m, account.EstimatedBalance);
        Assert.Equal(BalanceType.Verified, account.BalanceKind);
    }

    [Fact]
    public void An_account_with_no_verified_anchor_is_always_estimated()
    {
        var account = NewAccount();
        account.ApplyMovement(120m, Now, Now);

        Assert.Equal(BalanceType.Estimated, account.BalanceKind);
        Assert.Equal(120m, account.EstimatedBalance);
    }

    [Fact]
    public void Rebuilding_the_estimate_reproduces_the_anchor_plus_the_net_delta()
    {
        var account = NewAccount(1000m);
        account.ApplyMovement(-40m, Now.AddDays(1), Now);

        account.RebuildEstimate(-90m, Now.AddDays(3), Now);

        Assert.Equal(910m, account.EstimatedBalance);
    }

    [Fact]
    public void Opening_an_account_in_an_unsupported_mode_is_rejected()
    {
        var provider = Provider.Create(
            ProviderCodes.Deuna, "DEUNA", "DEUNA", ProviderKind.Wallet,
            [ConnectionMode.ManualImport], Now);

        var error = Assert.Throws<DomainException>(() => FinancialAccount.Open(
            Guid.CreateVersion7(), provider, "DEUNA", AccountType.Wallet, ConnectionMode.Api, Now));

        Assert.Equal("unsupported_connection_mode", error.Code);
    }

    [Fact]
    public void Only_the_last_four_digits_of_an_account_number_are_kept()
    {
        var provider = Provider.Create(
            ProviderCodes.Pichincha, "Banco Pichincha", "Pichincha", ProviderKind.Bank,
            [ConnectionMode.ManualImport], Now);

        var account = FinancialAccount.Open(
            Guid.CreateVersion7(), provider, "Cuenta", AccountType.Savings,
            ConnectionMode.ManualImport, Now, "2100 1234 5678 4821");

        Assert.Equal("4821", account.Mask);
    }
}
