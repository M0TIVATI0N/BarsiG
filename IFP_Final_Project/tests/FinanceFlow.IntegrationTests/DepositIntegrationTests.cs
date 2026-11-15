using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Banking;
using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceFlow.IntegrationTests;

/// <summary>
/// Tests of the savings deposits against a real SQLite database.
/// They prove that the money of a deposit is really moved, that the interest is paid only
/// once for a month and that the total supply of the simulation never changes.
/// </summary>
public sealed class DepositIntegrationTests
{
    [Fact]
    public async Task OpenAsync_CreatesADepositWithExactlyZero()
    {
        using BankingFixture fixture = new();

        BankAccount primary = await fixture.CreateCustomerAsync("owner@example.com", "Owner");

        var result = await fixture.RunAsync(async provider =>
            await provider.GetRequiredService<DepositService>().OpenAsync(
                primary.UserId!.Value, "Holiday", 12m, 6));

        Assert.True(result.IsSuccess());
        Assert.True(result.ValueOr(default!).Balance.IsZero);
    }

    [Fact]
    public async Task ContributeAsync_MovesTheMoneyFromThePrimaryAccountToTheDeposit()
    {
        using BankingFixture fixture = new();

        BankAccount primary = await fixture.CreateCustomerAsync("owner@example.com", "Owner");
        await fixture.FundAsync(primary, 100_000m);

        Guid depositId = await OpenDepositAsync(fixture, primary);

        var result = await fixture.RunAsync(async provider =>
            await provider.GetRequiredService<DepositService>().ContributeAsync(
                primary.UserId!.Value, depositId, 30_000m));

        Assert.True(result.IsSuccess());
        Assert.Equal(70_000_00, (await fixture.ReadBalanceAsync(primary.Id)).Cents);
        Assert.Equal(30_000_00, (await fixture.ReadBalanceAsync(await ReadDepositAccountAsync(fixture, depositId))).Cents);
    }

    [Fact]
    public async Task AccrueInterestAsync_PaysTheMonthlyInterestFromTheReserve()
    {
        using BankingFixture fixture = new();

        BankAccount primary = await fixture.CreateCustomerAsync("owner@example.com", "Owner");
        await fixture.FundAsync(primary, 100_000m);

        Guid depositId = await OpenDepositAsync(fixture, primary);

        await fixture.RunAsync(async provider =>
            await provider.GetRequiredService<DepositService>().ContributeAsync(
                primary.UserId!.Value, depositId, 30_000m));

        long reserveBefore = await ReadReserveAsync(fixture);

        await AdvanceClockAsync(fixture, 32);

        int accrued = await fixture.RunAsync(async provider =>
            (await provider.GetRequiredService<DepositService>().AccrueInterestAsync()).ValueOr(0));

        Assert.Equal(1, accrued);

        Guid depositAccount = await ReadDepositAccountAsync(fixture, depositId);

        // 30 000 * 12 % / 12 = 300
        Assert.Equal(30_300_00, (await fixture.ReadBalanceAsync(depositAccount)).Cents);

        long reserveAfter = await ReadReserveAsync(fixture);

        Assert.Equal(reserveBefore - 300_00, reserveAfter);
    }

    [Fact]
    public async Task AccrueInterestAsync_NeverPaysTheSameMonthTwice()
    {
        using BankingFixture fixture = new();

        BankAccount primary = await fixture.CreateCustomerAsync("owner@example.com", "Owner");
        await fixture.FundAsync(primary, 100_000m);

        Guid depositId = await OpenDepositAsync(fixture, primary);

        await fixture.RunAsync(async provider =>
            await provider.GetRequiredService<DepositService>().ContributeAsync(
                primary.UserId!.Value, depositId, 30_000m));

        await AdvanceClockAsync(fixture, 32);

        await fixture.RunAsync(async provider =>
            await provider.GetRequiredService<DepositService>().AccrueInterestAsync());

        Guid depositAccount = await ReadDepositAccountAsync(fixture, depositId);
        long balanceAfterFirstRun = (await fixture.ReadBalanceAsync(depositAccount)).Cents;

        // A second run of the job must not change anything.
        int second = await fixture.RunAsync(async provider =>
            (await provider.GetRequiredService<DepositService>().AccrueInterestAsync()).ValueOr(0));

        Assert.Equal(0, second);
        Assert.Equal(balanceAfterFirstRun, (await fixture.ReadBalanceAsync(depositAccount)).Cents);
    }

    [Fact]
    public async Task WithdrawAsync_ClosesTheDepositWhenTheBalanceFallsToZero()
    {
        using BankingFixture fixture = new();

        BankAccount primary = await fixture.CreateCustomerAsync("owner@example.com", "Owner");
        await fixture.FundAsync(primary, 100_000m);

        Guid depositId = await OpenDepositAsync(fixture, primary);

        await fixture.RunAsync(async provider =>
            await provider.GetRequiredService<DepositService>().ContributeAsync(
                primary.UserId!.Value, depositId, 30_000m));

        var result = await fixture.RunAsync(async provider =>
            await provider.GetRequiredService<DepositService>().WithdrawAsync(
                primary.UserId!.Value, depositId, 0m));

        Assert.True(result.IsSuccess());
        Assert.Equal(100_000_00, (await fixture.ReadBalanceAsync(primary.Id)).Cents);

        DepositStatus status = await fixture.RunAsync(async provider =>
            (await provider.GetRequiredService<IDepositRepository>().GetByIdAsync(depositId, primary.UserId!.Value))!.Status);

        Assert.Equal(DepositStatus.Closed, status);
    }

    [Fact]
    public async Task TheDepositDoesNotBelongToAnotherCustomer()
    {
        using BankingFixture fixture = new();

        BankAccount owner = await fixture.CreateCustomerAsync("owner@example.com", "Owner");
        BankAccount stranger = await fixture.CreateCustomerAsync("stranger@example.com", "Stranger");
        await fixture.FundAsync(owner, 100_000m);

        Guid depositId = await OpenDepositAsync(fixture, owner);

        var result = await fixture.RunAsync(async provider =>
            await provider.GetRequiredService<DepositService>().ContributeAsync(
                stranger.UserId!.Value, depositId, 100m));

        Assert.True(result.IsFailure());
        Assert.Equal("deposit_not_found", ((Fail<Money>)result).Code);
    }

    private static async Task<Guid> OpenDepositAsync(BankingFixture fixture, BankAccount primary)
    {
        var result = await fixture.RunAsync(async provider =>
            await provider.GetRequiredService<DepositService>().OpenAsync(
                primary.UserId!.Value, "Holiday", 12m, 6));

        Assert.True(result.IsSuccess());

        return result.ValueOr(default!).Id;
    }

    private static async Task<Guid> ReadDepositAccountAsync(BankingFixture fixture, Guid depositId)
    {
        return await fixture.RunAsync(async provider =>
        {
            Deposit? deposit = await provider.GetRequiredService<IDepositRepository>().GetByIdAsync(
                depositId,
                (await provider.GetRequiredService<IBankAccountRepository>().GetAllAsync())
                    .First(account => account.Type == BankAccountType.Primary).UserId ?? Guid.Empty);

            return deposit!.BankAccountId;
        });
    }

    private static async Task<long> ReadReserveAsync(BankingFixture fixture)
    {
        return await fixture.RunAsync(async provider =>
        {
            BankAccount reserve = (await provider.GetRequiredService<IBankAccountRepository>().GetReserveAsync())!;

            return reserve.Balance.Cents;
        });
    }

    private static async Task AdvanceClockAsync(BankingFixture fixture, int days)
    {
        await fixture.RunAsync(async provider =>
        {
            provider.GetRequiredService<FinanceFlow.Infrastructure.Time.AdjustableClock>().Advance(TimeSpan.FromDays(days));

            return true;
        });
    }
}
