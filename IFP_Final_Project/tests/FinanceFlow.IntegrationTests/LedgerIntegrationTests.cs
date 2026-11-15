using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Banking;
using FinanceFlow.Application.Banking.Dtos;
using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceFlow.IntegrationTests;

/// <summary>
/// Tests of the banking ledger against a real SQLite database.
/// They prove the invariants of the bank: the money is moved, never created,
/// a failed movement changes nothing and a repeated request moves the money only once.
/// </summary>
public sealed class LedgerIntegrationTests
{
    [Fact]
    public async Task ExecuteAsync_MovesTheMoneyAndWritesTwoLedgerEntries()
    {
        using BankingFixture fixture = new();

        BankAccount sender = await fixture.CreateCustomerAsync("sender@example.com", "Sender");
        BankAccount receiver = await fixture.CreateCustomerAsync("receiver@example.com", "Receiver");

        await fixture.FundAsync(sender, 100_000m);

        Guid transferId = await fixture.RunAsync(async provider =>
        {
            ILedgerExecutor executor = provider.GetRequiredService<ILedgerExecutor>();

            Result<LedgerTransfer> result = await executor.ExecuteAsync(new LedgerExecutionRequest(
                LedgerTransferKind.UserTransfer,
                sender.Id,
                receiver.Id,
                sender.UserId!.Value,
                Money.FromDecimal(25_000m),
                "Test transfer",
                Guid.NewGuid().ToString()));

            Assert.True(result.IsSuccess());

            return result.ValueOr(default!).Id;
        });

        // 100 000 - 25 000 = 75 000
        Assert.Equal(75_000_00, (await fixture.ReadBalanceAsync(sender.Id)).Cents);
        Assert.Equal(25_000_00, (await fixture.ReadBalanceAsync(receiver.Id)).Cents);

        await fixture.RunAsync(async provider =>
        {
            FinanceFlow.Infrastructure.Persistence.FinanceFlowDbContext dbContext =
                provider.GetRequiredService<FinanceFlow.Infrastructure.Persistence.FinanceFlowDbContext>();

            LedgerEntry[] entries = await dbContext.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.TransferId == transferId)
                .ToArrayAsync();

            Assert.Equal(2, entries.Length);
            Assert.Equal(1, entries.Count(entry => entry.Direction == LedgerEntryDirection.Debit));
            Assert.Equal(1, entries.Count(entry => entry.Direction == LedgerEntryDirection.Credit));
        });
    }

    [Fact]
    public async Task ExecuteAsync_KeepsTheTotalSupplyOfTheSimulationConstant()
    {
        using BankingFixture fixture = new();

        BankAccount sender = await fixture.CreateCustomerAsync("sender@example.com", "Sender");
        BankAccount receiver = await fixture.CreateCustomerAsync("receiver@example.com", "Receiver");

        long supplyBefore = await fixture.ReadTotalSupplyAsync();

        await fixture.FundAsync(sender, 50_000m);

        long supplyAfterFunding = await fixture.ReadTotalSupplyAsync();

        Assert.Equal(supplyBefore, supplyAfterFunding);

        await fixture.RunAsync(async provider =>
        {
            ILedgerExecutor executor = provider.GetRequiredService<ILedgerExecutor>();

            for (int index = 0; index < 5; index++)
            {
                Result<LedgerTransfer> result = await executor.ExecuteAsync(new LedgerExecutionRequest(
                    LedgerTransferKind.UserTransfer,
                    sender.Id,
                    receiver.Id,
                    sender.UserId!.Value,
                    Money.FromDecimal(1000m),
                    $"Transfer {index}",
                    Guid.NewGuid().ToString()));

                Assert.True(result.IsSuccess());
            }
        });

        Assert.Equal(supplyBefore, await fixture.ReadTotalSupplyAsync());
    }

    [Fact]
    public async Task ExecuteAsync_RejectsATransferWithoutEnoughMoneyAndChangesNothing()
    {
        using BankingFixture fixture = new();

        BankAccount sender = await fixture.CreateCustomerAsync("sender@example.com", "Sender");
        BankAccount receiver = await fixture.CreateCustomerAsync("receiver@example.com", "Receiver");

        await fixture.FundAsync(sender, 100m);

        await fixture.RunAsync(async provider =>
        {
            ILedgerExecutor executor = provider.GetRequiredService<ILedgerExecutor>();

            Result<LedgerTransfer> result = await executor.ExecuteAsync(new LedgerExecutionRequest(
                LedgerTransferKind.UserTransfer,
                sender.Id,
                receiver.Id,
                sender.UserId!.Value,
                Money.FromDecimal(1000m),
                "Too much",
                Guid.NewGuid().ToString()));

            Assert.True(result.IsFailure());
            Assert.Equal("insufficient_funds", ((Fail<LedgerTransfer>)result).Code);
        });

        Assert.Equal(100_00, (await fixture.ReadBalanceAsync(sender.Id)).Cents);
        Assert.True((await fixture.ReadBalanceAsync(receiver.Id)).IsZero);

        await fixture.RunAsync(async provider =>
        {
            FinanceFlow.Infrastructure.Persistence.FinanceFlowDbContext dbContext =
                provider.GetRequiredService<FinanceFlow.Infrastructure.Persistence.FinanceFlowDbContext>();

            // Only the funding of the reserve produced entries, the failed transfer produced none.
            Assert.Equal(0, await dbContext.LedgerEntries.CountAsync(entry => entry.AccountId == receiver.Id));
            Assert.Equal(0, await dbContext.LedgerTransfers.CountAsync(transfer => transfer.ToAccountId == receiver.Id));
        });
    }

    [Fact]
    public async Task ExecuteAsync_ExecutesTheSameIdempotencyKeyOnlyOnce()
    {
        using BankingFixture fixture = new();

        BankAccount sender = await fixture.CreateCustomerAsync("sender@example.com", "Sender");
        BankAccount receiver = await fixture.CreateCustomerAsync("receiver@example.com", "Receiver");

        string key = Guid.NewGuid().ToString();

        await fixture.FundAsync(sender, 1000m);

        await fixture.RunAsync(async provider =>
        {
            ILedgerExecutor executor = provider.GetRequiredService<ILedgerExecutor>();

            Result<LedgerTransfer> first = await executor.ExecuteAsync(new LedgerExecutionRequest(
                LedgerTransferKind.UserTransfer,
                sender.Id,
                receiver.Id,
                sender.UserId!.Value,
                Money.FromDecimal(500m),
                "First",
                key));

            Result<LedgerTransfer> second = await executor.ExecuteAsync(new LedgerExecutionRequest(
                LedgerTransferKind.UserTransfer,
                sender.Id,
                receiver.Id,
                sender.UserId!.Value,
                Money.FromDecimal(500m),
                "First",
                key));

            Assert.True(first.IsSuccess());
            Assert.True(second.IsSuccess());
            Assert.Equal(first.ValueOr(default!).Id, second.ValueOr(default!).Id);
        });

        Assert.Equal(500_00, (await fixture.ReadBalanceAsync(receiver.Id)).Cents);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsChangedPayloadForAnAlreadyUsedIdempotencyKey()
    {
        using BankingFixture fixture = new();
        BankAccount sender = await fixture.CreateCustomerAsync("sender@example.com", "Sender");
        BankAccount receiver = await fixture.CreateCustomerAsync("receiver@example.com", "Receiver");
        string key = Guid.NewGuid().ToString();
        await fixture.FundAsync(sender, 1_000m);

        await fixture.RunAsync(async provider =>
        {
            ILedgerExecutor executor = provider.GetRequiredService<ILedgerExecutor>();

            Result<LedgerTransfer> first = await executor.ExecuteAsync(new LedgerExecutionRequest(
                LedgerTransferKind.UserTransfer,
                sender.Id,
                receiver.Id,
                sender.UserId!.Value,
                Money.FromDecimal(500m),
                "Original payload",
                key));

            Result<LedgerTransfer> changedPayload = await executor.ExecuteAsync(new LedgerExecutionRequest(
                LedgerTransferKind.UserTransfer,
                sender.Id,
                receiver.Id,
                sender.UserId.Value,
                Money.FromDecimal(400m),
                "Changed payload",
                key));

            Assert.True(first.IsSuccess());
            Assert.True(changedPayload.IsFailure());
            Assert.Equal("idempotency_conflict", ((Fail<LedgerTransfer>)changedPayload).Code);
        });

        Assert.Equal(500_00, (await fixture.ReadBalanceAsync(sender.Id)).Cents);
        Assert.Equal(500_00, (await fixture.ReadBalanceAsync(receiver.Id)).Cents);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsAccountsWithDifferentCurrenciesWithoutChangingBalances()
    {
        using BankingFixture fixture = new();
        BankAccount sender = await fixture.CreateCustomerAsync("sender@example.com", "Sender");
        BankAccount receiver = await fixture.CreateCustomerAsync("receiver@example.com", "Receiver");
        await fixture.FundAsync(sender, 1_000m);

        await fixture.RunAsync(async provider =>
        {
            FinanceFlow.Infrastructure.Persistence.FinanceFlowDbContext db =
                provider.GetRequiredService<FinanceFlow.Infrastructure.Persistence.FinanceFlowDbContext>();
            BankAccount trackedReceiver = await db.BankAccounts.SingleAsync(account => account.Id == receiver.Id);
            db.Entry(trackedReceiver).CurrentValues.SetValues(trackedReceiver with { Currency = "USD" });
            await db.SaveChangesAsync();

            Result<LedgerTransfer> result = await provider.GetRequiredService<ILedgerExecutor>().ExecuteAsync(
                new LedgerExecutionRequest(
                    LedgerTransferKind.UserTransfer,
                    sender.Id,
                    receiver.Id,
                    sender.UserId!.Value,
                    Money.FromDecimal(100m),
                    "Incompatible currencies",
                    Guid.NewGuid().ToString()));

            Assert.True(result.IsFailure());
            Assert.Equal("currency_mismatch", ((Fail<LedgerTransfer>)result).Code);
        });

        Assert.Equal(100_000, (await fixture.ReadBalanceAsync(sender.Id)).Cents);
        Assert.True((await fixture.ReadBalanceAsync(receiver.Id)).IsZero);
    }

    [Fact]
    public async Task ExecuteAsync_LetsOnlyOneOfTwoConcurrentTransfersSucceed()
    {
        using BankingFixture fixture = new();

        BankAccount sender = await fixture.CreateCustomerAsync("sender@example.com", "Sender");
        BankAccount first = await fixture.CreateCustomerAsync("first@example.com", "First");
        BankAccount second = await fixture.CreateCustomerAsync("second@example.com", "Second");

        // The sender receives exactly one unit of money.
        await fixture.FundAsync(sender, 100m);

        Task<Result<LedgerTransfer>>[] tasks =
        [
            ExecuteAsync(fixture, sender, first, "concurrent-1"),
            ExecuteAsync(fixture, sender, second, "concurrent-2")
        ];

        Result<LedgerTransfer>[] results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(result => result.IsSuccess()));
        Assert.Equal(1, results.Count(result => result.IsFailure()));

        // The sender had exactly one transfer worth of money, so one of the two
        // concurrent transfers took everything and the other one was rejected.
        Assert.Equal(0, (await fixture.ReadBalanceAsync(sender.Id)).Cents);
    }

    [Fact]
    public async Task ExecuteAsync_EnforcesDailyLimitAcrossConcurrentTransfers()
    {
        using BankingFixture fixture = new(initialReserve: 3_000_000m);
        BankAccount sender = await fixture.CreateCustomerAsync("limit-sender@example.com", "Sender");
        BankAccount first = await fixture.CreateCustomerAsync("limit-first@example.com", "First");
        BankAccount second = await fixture.CreateCustomerAsync("limit-second@example.com", "Second");
        await fixture.FundAsync(sender, 2_200_000m);

        Result<LedgerTransfer> initial = await fixture.RunAsync(provider =>
            provider.GetRequiredService<ILedgerExecutor>().ExecuteAsync(new LedgerExecutionRequest(
                LedgerTransferKind.UserTransfer,
                sender.Id,
                first.Id,
                sender.UserId!.Value,
                Money.FromDecimal(1_900_000m),
                "Approach daily limit",
                "daily-limit-initial")));
        Assert.True(initial.IsSuccess());

        Task<Result<LedgerTransfer>>[] concurrentTransfers =
        [
            ExecuteAmountAsync(fixture, sender, first, 75_000m, "daily-limit-concurrent-1"),
            ExecuteAmountAsync(fixture, sender, second, 75_000m, "daily-limit-concurrent-2")
        ];

        Result<LedgerTransfer>[] results = await Task.WhenAll(concurrentTransfers);

        Assert.Single(results, result => result.IsSuccess());
        Fail<LedgerTransfer> rejection = Assert.IsType<Fail<LedgerTransfer>>(
            Assert.Single(results, result => result.IsFailure()));
        Assert.Equal("daily_limit_exceeded", rejection.Code);

        // The sender had enough money for both requests; only the daily cap rejects one.
        Assert.Equal(225_000_00, (await fixture.ReadBalanceAsync(sender.Id)).Cents);
    }

    [Fact]
    public async Task TransferService_DailyLimitCountsMoreThanTheLatestFiveHundredTransfers()
    {
        using BankingFixture fixture = new(initialReserve: 3_000_000m);
        BankAccount sender = await fixture.CreateCustomerAsync("history-sender@example.com", "Sender");
        BankAccount receiver = await fixture.CreateCustomerAsync("history-receiver@example.com", "Receiver");
        await fixture.FundAsync(sender, 2_100_000m);

        for (int index = 0; index < 501; index++)
        {
            Result<LedgerTransfer> result = await fixture.RunAsync(provider =>
                provider.GetRequiredService<ILedgerExecutor>().ExecuteAsync(new LedgerExecutionRequest(
                    LedgerTransferKind.UserTransfer,
                    sender.Id,
                    receiver.Id,
                    sender.UserId!.Value,
                    Money.FromDecimal(3_990m),
                    "Transfer for daily-limit regression",
                    $"daily-limit-history-{index}")));

            Assert.True(result.IsSuccess(), $"Transfer {index + 1} should remain below the daily cap.");
        }

        Result<TransferPreview> preview = await fixture.RunAsync(provider =>
            provider.GetRequiredService<TransferService>().PreviewAsync(
                sender.UserId!.Value,
                new TransferRequest(
                    receiver.UserId!.Value,
                    3_500m,
                    "Exceed the daily limit",
                    "daily-limit-history-overflow")));

        Assert.True(preview.IsFailure());
        Assert.Equal("daily_limit_exceeded", ((Fail<TransferPreview>)preview).Code);
        Assert.Equal(1_998_990_00, (await fixture.ReadBalanceAsync(receiver.Id)).Cents);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsAnUnknownAccount()
    {
        using BankingFixture fixture = new();

        BankAccount sender = await fixture.CreateCustomerAsync("sender@example.com", "Sender");

        await fixture.RunAsync(async provider =>
        {
            ILedgerExecutor executor = provider.GetRequiredService<ILedgerExecutor>();

            Result<LedgerTransfer> result = await executor.ExecuteAsync(new LedgerExecutionRequest(
                LedgerTransferKind.UserTransfer,
                sender.Id,
                Guid.NewGuid(),
                sender.UserId!.Value,
                Money.FromDecimal(10m),
                "Unknown",
                Guid.NewGuid().ToString()));

            Assert.True(result.IsFailure());
            Assert.Equal("account_not_found", ((Fail<LedgerTransfer>)result).Code);
        });
    }

    [Fact]
    public async Task ExecuteAsync_RejectsATransferToTheSameAccount()
    {
        using BankingFixture fixture = new();

        BankAccount sender = await fixture.CreateCustomerAsync("sender@example.com", "Sender");

        await fixture.RunAsync(async provider =>
        {
            ILedgerExecutor executor = provider.GetRequiredService<ILedgerExecutor>();

            Result<LedgerTransfer> result = await executor.ExecuteAsync(new LedgerExecutionRequest(
                LedgerTransferKind.UserTransfer,
                sender.Id,
                sender.Id,
                sender.UserId!.Value,
                Money.FromDecimal(10m),
                "Same",
                Guid.NewGuid().ToString()));

            Assert.True(result.IsFailure());
            Assert.Equal("same_account", ((Fail<LedgerTransfer>)result).Code);
        });
    }

    [Fact]
    public async Task TheStoredBalanceAlwaysAgreesWithTheLedger()
    {
        using BankingFixture fixture = new();

        BankAccount sender = await fixture.CreateCustomerAsync("sender@example.com", "Sender");
        BankAccount receiver = await fixture.CreateCustomerAsync("receiver@example.com", "Receiver");

        await fixture.FundAsync(sender, 1000m);

        await fixture.RunAsync(async provider =>
        {
            ILedgerExecutor executor = provider.GetRequiredService<ILedgerExecutor>();
            ILedgerRepository ledger = provider.GetRequiredService<ILedgerRepository>();
            IBankAccountRepository accounts = provider.GetRequiredService<IBankAccountRepository>();

            await executor.ExecuteAsync(new LedgerExecutionRequest(
                LedgerTransferKind.UserTransfer,
                sender.Id,
                receiver.Id,
                sender.UserId!.Value,
                Money.FromDecimal(300m),
                "First",
                Guid.NewGuid().ToString()));

            await executor.ExecuteAsync(new LedgerExecutionRequest(
                LedgerTransferKind.UserTransfer,
                receiver.Id,
                sender.Id,
                receiver.UserId!.Value,
                Money.FromDecimal(100m),
                "Second",
                Guid.NewGuid().ToString()));

            foreach (BankAccount account in await accounts.GetAllAsync())
            {
                Money computed = await ledger.ComputeBalanceAsync(account.Id);
                Money stored = (await accounts.GetByIdAsync(account.Id))!.Balance;

                Assert.Equal(computed.Cents, stored.Cents);
            }
        });
    }

    [Fact]
    public async Task AUserCannotReadTheAccountOfSomebodyElse()
    {
        using BankingFixture fixture = new();

        BankAccount owner = await fixture.CreateCustomerAsync("owner@example.com", "Owner");
        BankAccount stranger = await fixture.CreateCustomerAsync("stranger@example.com", "Stranger");

        BankAccount? found = await fixture.RunAsync(async provider =>
            await provider.GetRequiredService<IBankAccountRepository>().GetOwnedAsync(stranger.Id, owner.UserId!.Value));

        Assert.Null(found);

        BankAccount? own = await fixture.RunAsync(async provider =>
            await provider.GetRequiredService<IBankAccountRepository>().GetOwnedAsync(owner.Id, owner.UserId!.Value));

        Assert.NotNull(own);
    }

    private static Task<Result<LedgerTransfer>> ExecuteAsync(
        BankingFixture fixture,
        BankAccount sender,
        BankAccount receiver,
        string key) =>
        fixture.RunAsync(async provider =>
        {
            ILedgerExecutor executor = provider.GetRequiredService<ILedgerExecutor>();

            return await executor.ExecuteAsync(new LedgerExecutionRequest(
                LedgerTransferKind.UserTransfer,
                sender.Id,
                receiver.Id,
                sender.UserId!.Value,
                Money.FromDecimal(100m),
                "Concurrent",
                key));
        });

    private static Task<Result<LedgerTransfer>> ExecuteAmountAsync(
        BankingFixture fixture,
        BankAccount sender,
        BankAccount receiver,
        decimal amount,
        string key) =>
        fixture.RunAsync(async provider =>
        {
            ILedgerExecutor executor = provider.GetRequiredService<ILedgerExecutor>();

            return await executor.ExecuteAsync(new LedgerExecutionRequest(
                LedgerTransferKind.UserTransfer,
                sender.Id,
                receiver.Id,
                sender.UserId!.Value,
                Money.FromDecimal(amount),
                "Concurrent daily-limit regression",
                key));
        });
}
