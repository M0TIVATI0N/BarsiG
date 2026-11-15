using FinanceFlow.Application.Banking;
using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Common;
using FinanceFlow.UnitTests.Fakes;

namespace FinanceFlow.UnitTests;

/// <summary>Tests of the pure banking rules and of the account service.</summary>
public sealed class BankingTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 12.0, 0)]
    [InlineData(100000, 12.0, 1000)]
    [InlineData(300000, 12.0, 3000)]
    [InlineData(10000, 6.0, 50)]
    public void CalculateMonthlyInterest_CalculatesTheInterestOfOneMonth(
        long principalCents,
        double rate,
        long expectedCents)
    {
        Money interest = BankingMath.CalculateMonthlyInterest(new Money(principalCents), (decimal)rate);

        Assert.Equal(expectedCents, interest.Cents);
    }

    [Fact]
    public void CalculateMonthlyInterest_RoundsToWholeCents()
    {
        // 1000.01 * 0.12 / 12 = 10.0001 -> 10.00
        Money interest = BankingMath.CalculateMonthlyInterest(Money.FromDecimal(1000.01m), 12m);

        Assert.Equal(1000, interest.Cents);
    }

    [Fact]
    public void CalculateInterest_ReturnsZeroForANonPositivePrincipal()
    {
        Assert.True(BankingMath.CalculateMonthlyInterest(Money.Zero, 12m).IsZero);
        Assert.True(BankingMath.CalculateMonthlyInterest(Money.FromDecimal(100m), 0m).IsZero);
    }

    [Fact]
    public void CalculateInterest_MultipliesTheMonthlyInterest()
    {
        Money interest = BankingMath.CalculateInterest(Money.FromDecimal(300000m), 12m, 3);

        Assert.Equal(900000, interest.Cents);
    }

    [Fact]
    public void SignedCents_IsNegativeForADebitAndPositiveForACredit()
    {
        Money amount = Money.FromDecimal(100m);

        Assert.Equal(-10000, BankingMath.SignedCents(LedgerEntryDirection.Debit, amount));
        Assert.Equal(10000, BankingMath.SignedCents(LedgerEntryDirection.Credit, amount));
    }

    [Theory]
    [InlineData(0, "zero_amount")]
    [InlineData(-500, "negative_amount")]
    [InlineData(50, "below_minimum")]
    [InlineData(200_000_000, "above_maximum")]
    public void ValidateAmount_RejectsTheAmountsOutsideTheLimits(long cents, string expectedCode)
    {
        Result<Money> result = BankingMath.ValidateAmount(new Money(cents), BankingLimits.Default);

        Assert.True(result.IsFailure());
        Assert.Equal(expectedCode, ((Fail<Money>)result).Code);
    }

    [Fact]
    public void ValidateAmount_AcceptsAnAmountInsideTheLimits()
    {
        Result<Money> result = BankingMath.ValidateAmount(Money.FromDecimal(1000m), BankingLimits.Default);

        Assert.True(result.IsSuccess());
    }

    [Fact]
    public void ValidateDailyLimit_RejectsAnAmountAboveTheDailyLimit()
    {
        Result<Money> result = BankingMath.ValidateDailyLimit(
            Money.FromDecimal(1_900_000m),
            Money.FromDecimal(200_000m),
            BankingLimits.Default);

        Assert.True(result.IsFailure());
        Assert.Equal("daily_limit_exceeded", ((Fail<Money>)result).Code);
    }

    [Fact]
    public void BuildAccountNumber_GroupsTheDigitsInBlocksOfFour()
    {
        Assert.Equal("KZ00 0000 0000 0042", BankingMath.BuildAccountNumber("KZ00", 42));
    }

    [Fact]
    public async Task EnsurePrimaryAccountAsync_CreatesAnAccountWithExactlyZero()
    {
        FakeBankAccountRepository accounts = new();
        AccountService service = new(
            accounts,
            new FakeLedgerRepository(),
            new FakeUnitOfWork(),
            new FixedClock(Now));

        Guid userId = Guid.NewGuid();

        var result = await service.EnsurePrimaryAccountAsync(userId);

        Assert.True(result.IsSuccess());
        Assert.True(result.ValueOr(default!).Balance.IsZero);
        Assert.Equal(BankAccountType.Primary, result.ValueOr(default!).Type);
        Assert.Single(await accounts.GetForUserAsync(userId));
    }

    [Fact]
    public async Task EnsurePrimaryAccountAsync_DoesNotCreateASecondAccount()
    {
        FakeBankAccountRepository accounts = new();
        AccountService service = new(
            accounts,
            new FakeLedgerRepository(),
            new FakeUnitOfWork(),
            new FixedClock(Now));

        Guid userId = Guid.NewGuid();

        await service.EnsurePrimaryAccountAsync(userId);
        await service.EnsurePrimaryAccountAsync(userId);

        Assert.Single(await accounts.GetForUserAsync(userId));
    }

    [Fact]
    public async Task GetAccountsAsync_ReturnsOnlyTheAccountsOfTheUser()
    {
        FakeBankAccountRepository accounts = new();
        Guid owner = Guid.NewGuid();
        Guid other = Guid.NewGuid();

        accounts.Seed(CreateAccount(owner, BankAccountType.Primary, "KZ00 0000 0000 0010"));
        accounts.Seed(CreateAccount(other, BankAccountType.Primary, "KZ00 0000 0000 0011"));

        AccountService service = new(
            accounts,
            new FakeLedgerRepository(),
            new FakeUnitOfWork(),
            new FixedClock(Now));

        IReadOnlyList<FinanceFlow.Application.Banking.Dtos.AccountSummary> result =
            await service.GetAccountsAsync(owner);

        Assert.Single(result);
        Assert.Equal("KZ00 0000 0000 0010", result[0].AccountNumber);
    }

    [Fact]
    public async Task GetTotalBalanceAsync_SumsTheBalancesOfTheUser()
    {
        FakeBankAccountRepository accounts = new();
        Guid owner = Guid.NewGuid();

        accounts.Seed(CreateAccount(owner, BankAccountType.Primary, "KZ00 0000 0000 0010", Money.FromDecimal(100m)));
        accounts.Seed(CreateAccount(owner, BankAccountType.Deposit, "KZ00 0000 0000 0011", Money.FromDecimal(50m)));

        AccountService service = new(
            accounts,
            new FakeLedgerRepository(),
            new FakeUnitOfWork(),
            new FixedClock(Now));

        Money total = await service.GetTotalBalanceAsync(owner);

        Assert.Equal(15000, total.Cents);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsTheMovementsOfTheUserOnly()
    {
        FakeBankAccountRepository accounts = new();
        FakeLedgerRepository ledger = new();
        Guid owner = Guid.NewGuid();
        Guid other = Guid.NewGuid();

        BankAccount own = CreateAccount(owner, BankAccountType.Primary, "KZ00 0000 0000 0010");
        BankAccount foreign = CreateAccount(other, BankAccountType.Primary, "KZ00 0000 0000 0011");

        accounts.Seed(own);
        accounts.Seed(foreign);

        LedgerTransfer transfer = new(
            Guid.NewGuid(),
            "key-1",
            LedgerTransferKind.UserTransfer,
            LedgerTransferStatus.Completed,
            own.Id,
            foreign.Id,
            owner,
            Money.FromDecimal(10m),
            "KZT",
            "Test",
            null,
            Now,
            Now);

        ledger.Seed(
            transfer,
            new LedgerEntry(Guid.NewGuid(), transfer.Id, own.Id, LedgerEntryDirection.Debit, Money.FromDecimal(10m), Money.Zero, Now),
            new LedgerEntry(Guid.NewGuid(), transfer.Id, foreign.Id, LedgerEntryDirection.Credit, Money.FromDecimal(10m), Money.FromDecimal(10m), Now));

        AccountService service = new(accounts, ledger, new FakeUnitOfWork(), new FixedClock(Now));

        IReadOnlyList<FinanceFlow.Application.Banking.Dtos.LedgerMovementItem> result =
            await service.GetHistoryAsync(owner, 50);

        Assert.Single(result);
        Assert.Equal(LedgerEntryDirection.Debit, result[0].Direction);
    }

    private static BankAccount CreateAccount(
        Guid userId,
        BankAccountType type,
        string number,
        Money? balance = null) =>
        new(
            Guid.NewGuid(),
            userId,
            number,
            type == BankAccountType.Primary ? "Primary account" : "Deposit",
            type,
            BankAccountStatus.Active,
            balance ?? Money.Zero,
            "KZT",
            0,
            Now,
            null);
}
