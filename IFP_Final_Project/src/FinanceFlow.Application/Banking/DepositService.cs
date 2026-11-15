using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Banking.Dtos;
using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Common;

namespace FinanceFlow.Application.Banking;

/// <summary>
/// The use cases of the savings deposits.
/// Every movement of the money is a ledger transfer, so the interest is paid from the reserve
/// of the Demo Bank and the total supply of the simulation never changes.
/// </summary>
public sealed class DepositService
{
    private readonly IBankAccountRepository _accounts;
    private readonly IDepositRepository _deposits;
    private readonly ILedgerExecutor _executor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDataChangeNotifier _notifier;
    private readonly BankingOptions _options;
    private readonly IClock _clock;

    /// <summary>Creates the deposit service.</summary>
    public DepositService(
        IBankAccountRepository accounts,
        IDepositRepository deposits,
        ILedgerExecutor executor,
        IUnitOfWork unitOfWork,
        IDataChangeNotifier notifier,
        BankingOptions options,
        IClock clock)
    {
        _accounts = accounts;
        _deposits = deposits;
        _executor = executor;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
        _options = options;
        _clock = clock;
    }

    /// <summary>Returns every deposit of the user with the balance of the deposit account.</summary>
    public async Task<IReadOnlyList<DepositItem>> GetDepositsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Deposit> deposits = await _deposits.GetForUserAsync(userId, cancellationToken);

        List<DepositItem> result = [];

        foreach (Deposit deposit in deposits)
        {
            BankAccount? account = await _accounts.GetByIdAsync(deposit.BankAccountId, cancellationToken);

            result.Add(ToItem(deposit, account?.Balance ?? Money.Zero));
        }

        return result;
    }

    /// <summary>Returns the interest payments of one deposit, newest first.</summary>
    public async Task<IReadOnlyList<DepositInterestItem>> GetInterestHistoryAsync(
        Guid userId,
        Guid depositId,
        CancellationToken cancellationToken = default)
    {
        Deposit? deposit = await _deposits.GetByIdAsync(depositId, userId, cancellationToken);

        if (deposit is null)
        {
            return [];
        }

        IReadOnlyList<DepositInterestAccrual> accruals =
            await _deposits.GetAccrualsAsync(depositId, cancellationToken);

        return accruals
            .Select(accrual => new DepositInterestItem(
                accrual.PeriodMonth,
                accrual.Interest,
                accrual.BalanceAfter,
                accrual.AccruedAtUtc))
            .ToList();
    }

    /// <summary>
    /// Opens a new deposit for the customer.
    /// The deposit starts with exactly zero, the customer fills it with a separate transfer.
    /// </summary>
    public async Task<Result<DepositItem>> OpenAsync(
        Guid userId,
        string name,
        decimal annualRatePercent,
        int termMonths,
        CancellationToken cancellationToken = default)
    {
        string title = (name ?? string.Empty).Trim();

        if (title.Length == 0)
        {
            return new Fail<DepositItem>("The name of the deposit is required.")
            {
                Code = "validation"
            };
        }

        if (annualRatePercent <= 0m || annualRatePercent > 100m)
        {
            return new Fail<DepositItem>("The yearly rate must be between 0 and 100 percent.")
            {
                Code = "validation"
            };
        }

        if (termMonths is < 1 or > 120)
        {
            return new Fail<DepositItem>("The term must be between 1 and 120 months.")
            {
                Code = "validation"
            };
        }

        BankAccount? primary = await _accounts.GetPrimaryAsync(userId, cancellationToken);

        if (primary is null)
        {
            return new Fail<DepositItem>("The primary account does not exist.")
            {
                Code = "account_not_found"
            };
        }

        IReadOnlyList<BankAccount> all = await _accounts.GetAllAsync(cancellationToken);

        BankAccount account = new(
            Guid.NewGuid(),
            userId,
            BankingMath.BuildAccountNumber(BankingMath.AccountNumberPrefix, all.Count + 1),
            title,
            BankAccountType.Deposit,
            BankAccountStatus.Active,
            Money.Zero,
            "KZT",
            0,
            _clock.UtcNow,
            null);

        DateOnly month = new(_clock.Today.Year, _clock.Today.Month, 1);

        Deposit deposit = new(
            Guid.NewGuid(),
            userId,
            account.Id,
            title,
            annualRatePercent,
            termMonths,
            DepositStatus.Active,
            month,
            _clock.UtcNow,
            null);

        await _accounts.AddAsync(account, cancellationToken);
        await _deposits.AddAsync(deposit, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _notifier.NotifyAsync(userId, "banking", "deposit_opened", cancellationToken);

        return new Ok<DepositItem>(ToItem(deposit, Money.Zero));
    }

    /// <summary>Moves money from the primary account of the customer into one of his deposits.</summary>
    public async Task<Result<Money>> ContributeAsync(
        Guid userId,
        Guid depositId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        Result<Deposit> deposit = await GetActiveDepositAsync(userId, depositId, cancellationToken);

        if (deposit.IsFailure())
        {
            return new Fail<Money>(deposit.Error) { Code = ((Fail<Deposit>)deposit).Code };
        }

        Money value = Money.FromDecimal(amount);

        Result<Money> check = BankingMath.ValidateAmount(value, _options.Limits);

        if (check.IsFailure())
        {
            return new Fail<Money>(check.Error) { Code = ((Fail<Money>)check).Code };
        }

        BankAccount? primary = await _accounts.GetPrimaryAsync(userId, cancellationToken);
        BankAccount? target = await _accounts.GetOwnedAsync(deposit.ValueOr(default!).BankAccountId, userId, cancellationToken);

        if (primary is null || target is null)
        {
            return new Fail<Money>("The account does not exist.")
            {
                Code = "account_not_found"
            };
        }

        Result<LedgerTransfer> executed = await _executor.ExecuteAsync(
            new LedgerExecutionRequest(
                LedgerTransferKind.DepositContribution,
                primary.Id,
                target.Id,
                userId,
                value,
                $"Contribution to {deposit.ValueOr(default!).Name}",
                Guid.NewGuid().ToString()),
            cancellationToken);

        if (executed.IsFailure())
        {
            return new Fail<Money>(executed.Error) { Code = ((Fail<LedgerTransfer>)executed).Code };
        }

        await _notifier.NotifyAsync(userId, "banking", "deposit_contribution", cancellationToken);

        return new Ok<Money>((await _accounts.GetByIdAsync(target.Id, cancellationToken))?.Balance ?? Money.Zero);
    }

    /// <summary>
    /// Moves money from a deposit back to the primary account of the customer.
    /// A deposit whose balance falls to zero is closed.
    /// </summary>
    public async Task<Result<Money>> WithdrawAsync(
        Guid userId,
        Guid depositId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        Result<Deposit> deposit = await GetActiveDepositAsync(userId, depositId, cancellationToken);

        if (deposit.IsFailure())
        {
            return new Fail<Money>(deposit.Error) { Code = ((Fail<Deposit>)deposit).Code };
        }

        Money value = Money.FromDecimal(amount);

        if (value.IsNegative)
        {
            return new Fail<Money>("The amount must not be negative.")
            {
                Code = "negative_amount"
            };
        }

        BankAccount? primary = await _accounts.GetPrimaryAsync(userId, cancellationToken);
        BankAccount? source = await _accounts.GetOwnedAsync(deposit.ValueOr(default!).BankAccountId, userId, cancellationToken);

        if (primary is null || source is null)
        {
            return new Fail<Money>("The account does not exist.")
            {
                Code = "account_not_found"
            };
        }

        Money withdraw = value.IsZero ? source.Balance : value;

        if (withdraw > source.Balance)
        {
            return new Fail<Money>("The deposit does not hold that much money.")
            {
                Code = "insufficient_funds"
            };
        }

        Result<LedgerTransfer> executed = await _executor.ExecuteAsync(
            new LedgerExecutionRequest(
                LedgerTransferKind.DepositWithdrawal,
                source.Id,
                primary.Id,
                userId,
                withdraw,
                $"Withdrawal from {deposit.ValueOr(default!).Name}",
                Guid.NewGuid().ToString()),
            cancellationToken);

        if (executed.IsFailure())
        {
            return new Fail<Money>(executed.Error) { Code = ((Fail<LedgerTransfer>)executed).Code };
        }

        if (withdraw == source.Balance)
        {
            await CloseAsync(deposit.ValueOr(default!), source, cancellationToken);
        }

        await _notifier.NotifyAsync(userId, "banking", "deposit_withdrawal", cancellationToken);

        return new Ok<Money>((await _accounts.GetByIdAsync(primary.Id, cancellationToken))?.Balance ?? Money.Zero);
    }

    /// <summary>
    /// Pays the monthly interest of every deposit that did not receive it yet.
    /// The payment comes from the reserve of the Demo Bank, therefore the total supply of the
    /// simulation stays the same. A month can never be paid twice.
    /// </summary>
    public async Task<Result<int>> AccrueInterestAsync(CancellationToken cancellationToken = default)
    {
        BankAccount? reserve = await _accounts.GetReserveAsync(cancellationToken);

        if (reserve is null)
        {
            return new Fail<int>("The reserve of the Demo Bank does not exist.")
            {
                Code = "reserve_not_found"
            };
        }

        IReadOnlyList<Deposit> deposits = await _deposits.GetActiveAsync(cancellationToken);

        DateOnly currentMonth = new(_clock.Today.Year, _clock.Today.Month, 1);
        int paid = 0;

        foreach (Deposit deposit in deposits)
        {
            DateOnly month = deposit.LastAccruedMonth.AddMonths(1);

            while (month <= currentMonth)
            {
                if (!await _deposits.HasAccrualAsync(deposit.Id, month, cancellationToken))
                {
                    await AccrueMonthAsync(deposit, reserve, month, cancellationToken);
                    paid++;
                }

                month = month.AddMonths(1);
            }
        }

        return new Ok<int>(paid);
    }

    /// <summary>Accrues the interest of one month of one deposit.</summary>
    private async Task AccrueMonthAsync(
        Deposit deposit,
        BankAccount reserve,
        DateOnly month,
        CancellationToken cancellationToken)
    {
        BankAccount? account = await _accounts.GetByIdAsync(deposit.BankAccountId, cancellationToken);

        if (account is null)
        {
            return;
        }

        Money interest = BankingMath.CalculateMonthlyInterest(account.Balance, deposit.AnnualRatePercent);

        if (interest.IsPositive)
        {
            // The key is built from the deposit and the month, so a repeated run of the
            // interest job can never pay the same month twice.
            string key = $"interest:{deposit.Id:N}:{month:yyyy-MM}";

            Result<LedgerTransfer> executed = await _executor.ExecuteAsync(
                new LedgerExecutionRequest(
                    LedgerTransferKind.InterestAccrual,
                    reserve.Id,
                    deposit.BankAccountId,
                    deposit.UserId,
                    interest,
                    $"Monthly interest of {deposit.Name} for {month:yyyy-MM}",
                    key),
                cancellationToken);

            if (executed.IsFailure())
            {
                return;
            }

            BankAccount? after = await _accounts.GetByIdAsync(deposit.BankAccountId, cancellationToken);

            await _deposits.AddAccrualAsync(
                new DepositInterestAccrual(
                    Guid.NewGuid(),
                    deposit.Id,
                    month,
                    interest,
                    after?.Balance ?? account.Balance,
                    executed.ValueOr(default!).Id,
                    _clock.UtcNow),
                cancellationToken);
        }

        await _deposits.UpdateAsync(deposit with { LastAccruedMonth = month });
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Closes a deposit whose balance fell to zero.</summary>
    private async Task CloseAsync(Deposit deposit, BankAccount account, CancellationToken cancellationToken)
    {
        await _deposits.UpdateAsync(deposit with
        {
            Status = DepositStatus.Closed,
            ClosedAtUtc = _clock.UtcNow
        });

        BankAccount closed = account with
        {
            Status = BankAccountStatus.Closed,
            ClosedAtUtc = _clock.UtcNow,
            Version = account.Version + 1
        };

        await _accounts.UpdateAsync(closed, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Reads an active deposit of the customer.</summary>
    private async Task<Result<Deposit>> GetActiveDepositAsync(
        Guid userId,
        Guid depositId,
        CancellationToken cancellationToken)
    {
        Deposit? deposit = await _deposits.GetByIdAsync(depositId, userId, cancellationToken);

        if (deposit is null)
        {
            return new Fail<Deposit>("The deposit does not exist.")
            {
                Code = "deposit_not_found"
            };
        }

        if (deposit.Status != DepositStatus.Active)
        {
            return new Fail<Deposit>("The deposit is closed.")
            {
                Code = "deposit_closed"
            };
        }

        return new Ok<Deposit>(deposit);
    }

    /// <summary>Builds the read model of a deposit.</summary>
    public static DepositItem ToItem(Deposit deposit, Money balance) =>
        new(
            deposit.Id,
            deposit.Name,
            balance,
            deposit.AnnualRatePercent,
            deposit.TermMonths,
            deposit.Status,
            deposit.LastAccruedMonth,
            deposit.OpenedAtUtc);
}
