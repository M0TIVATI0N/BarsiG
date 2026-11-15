using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Banking.Dtos;
using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Common;

namespace FinanceFlow.Application.Banking;

/// <summary>
/// The use cases of the administration of the Demo Bank.
/// The administrator can only move money out of the reserve, he never edits a balance.
/// Every movement is a real ledger transfer, so the conservation of the money is kept.
/// </summary>
public sealed class AdminService
{
    private readonly IBankAccountRepository _accounts;
    private readonly ILedgerRepository _ledger;
    private readonly ILedgerExecutor _executor;
    private readonly IUserRepository _users;
    private readonly ISecurityProfileRepository _profiles;
    private readonly BankingOptions _options;

    /// <summary>Creates the administration service.</summary>
    public AdminService(
        IBankAccountRepository accounts,
        ILedgerRepository ledger,
        ILedgerExecutor executor,
        IUserRepository users,
        ISecurityProfileRepository profiles,
        BankingOptions options)
    {
        _accounts = accounts;
        _ledger = ledger;
        _executor = executor;
        _users = users;
        _profiles = profiles;
        _options = options;
    }

    /// <summary>Returns <c>true</c> when the user is an administrator of the Demo Bank.</summary>
    public async Task<bool> IsAdminAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        UserSecurityProfile? profile = await _profiles.GetByUserIdAsync(userId, cancellationToken);

        return profile?.Role == UserRole.Admin;
    }

    /// <summary>
    /// Gives virtual money to a customer out of the reserve.
    /// The reserve decreases by exactly the amount the customer receives.
    /// </summary>
    public async Task<Result<AdminFundingOutcome>> FundCustomerAsync(
        Guid adminUserId,
        Guid customerUserId,
        decimal amount,
        string? description,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(adminUserId, cancellationToken))
        {
            return new Fail<AdminFundingOutcome>("Only an administrator of the Demo Bank may do this.")
            {
                Code = "not_an_administrator"
            };
        }

        if (customerUserId == adminUserId)
        {
            return new Fail<AdminFundingOutcome>("The administrator can not fund his own account from the reserve.")
            {
                Code = "same_user"
            };
        }

        Money value = Money.FromDecimal(amount);

        Result<Money> amountCheck = BankingMath.ValidateAmount(value, _options.Limits);

        if (amountCheck.IsFailure())
        {
            return new Fail<AdminFundingOutcome>(amountCheck.Error) { Code = ((Fail<Money>)amountCheck).Code };
        }

        BankAccount? reserve = await _accounts.GetReserveAsync(cancellationToken);

        if (reserve is null)
        {
            return new Fail<AdminFundingOutcome>("The reserve of the Demo Bank does not exist.")
            {
                Code = "reserve_not_found"
            };
        }

        BankAccount? customer = await _accounts.GetPrimaryAsync(customerUserId, cancellationToken);

        if (customer is null)
        {
            return new Fail<AdminFundingOutcome>("The customer does not have a primary account.")
            {
                Code = "account_not_found"
            };
        }

        if (reserve.Balance < value)
        {
            return new Fail<AdminFundingOutcome>("The reserve of the Demo Bank does not hold that much money.")
            {
                Code = "insufficient_funds"
            };
        }

        FinanceFlow.Domain.Entities.User? user = await _users.FindByIdAsync(customerUserId, cancellationToken);

        string text = string.IsNullOrWhiteSpace(description)
            ? $"Funding of {customer.DisplayName}"
            : description.Trim();

        string key = string.IsNullOrWhiteSpace(idempotencyKey)
            ? Guid.NewGuid().ToString()
            : idempotencyKey.Trim();

        Result<LedgerTransfer> executed = await _executor.ExecuteAsync(
            new LedgerExecutionRequest(
                LedgerTransferKind.AdminFunding,
                reserve.Id,
                customer.Id,
                adminUserId,
                value,
                text,
                key),
            cancellationToken);

        if (executed.IsFailure())
        {
            return new Fail<AdminFundingOutcome>(executed.Error) { Code = ((Fail<LedgerTransfer>)executed).Code };
        }

        BankAccount reserveAfter = (await _accounts.GetByIdAsync(reserve.Id, cancellationToken))!;
        BankAccount customerAfter = (await _accounts.GetByIdAsync(customer.Id, cancellationToken))!;

        return new Ok<AdminFundingOutcome>(new AdminFundingOutcome(
            executed.ValueOr(default!).Id,
            value,
            reserveAfter.Balance,
            customerAfter.Balance,
            user?.DisplayName ?? customer.DisplayName));
    }

    /// <summary>Returns every customer of the Demo Bank with the balance of his primary account.</summary>
    public async Task<IReadOnlyList<AdminCustomerItem>> GetCustomersAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<BankAccount> accounts = await _accounts.GetAllAsync(cancellationToken);

        List<AdminCustomerItem> customers = [];

        foreach (BankAccount account in accounts.Where(account => account.Type == BankAccountType.Primary))
        {
            if (account.UserId is null)
            {
                continue;
            }

            FinanceFlow.Domain.Entities.User? user =
                await _users.FindByIdAsync(account.UserId.Value, cancellationToken);

            customers.Add(new AdminCustomerItem(
                account.UserId.Value,
                user?.DisplayName ?? account.DisplayName,
                user?.Email ?? string.Empty,
                user?.Email.EndsWith("@phone.invalid", StringComparison.OrdinalIgnoreCase) == true
                    ? "Phone-only account"
                    : SecurityService.MaskEmail(user?.Email ?? string.Empty),
                account.Id,
                account.Balance));
        }

        return customers;
    }

    /// <summary>
    /// Returns the state of the Demo Bank.
    /// The method also checks the ledger: the stored balances have to agree with the entries.
    /// </summary>
    public async Task<AdminOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<BankAccount> accounts = await _accounts.GetAllAsync(cancellationToken);

        BankAccount? reserve = accounts.FirstOrDefault(account => account.Type == BankAccountType.Reserve);

        Money reserveBalance = reserve?.Balance ?? Money.Zero;
        Money customerBalance = accounts
            .Where(account => account.UserId is not null)
            .Aggregate(Money.Zero, (total, account) => total + account.Balance);

        IReadOnlyList<LedgerAccountBalance> computed = await _ledger.ComputeAllBalancesAsync(cancellationToken);
        Dictionary<Guid, Money> computedByAccount = computed.ToDictionary(balance => balance.AccountId, balance => balance.ComputedBalance);

        bool consistent = accounts.All(account =>
            !computedByAccount.TryGetValue(account.Id, out Money value) || value == account.Balance);

        int customers = accounts
            .Where(account => account.Type == BankAccountType.Primary)
            .Select(account => account.UserId)
            .Distinct()
            .Count();

        int transfers = await _ledger.CountTransfersAsync(cancellationToken);

        return new AdminOverview(
            reserveBalance,
            customerBalance,
            customers,
            accounts.Count,
            transfers,
            consistent);
    }
}
