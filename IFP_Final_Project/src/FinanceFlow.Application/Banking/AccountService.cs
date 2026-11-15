using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Banking.Dtos;
using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Common;

namespace FinanceFlow.Application.Banking;

/// <summary>
/// Reads the bank accounts and the ledger of a user.
/// The service never returns an account that does not belong to the caller,
/// so the isolation of the data is guaranteed here and not only in the interface layer.
/// </summary>
public sealed class AccountService
{
    /// <summary>The prefix of every account number of the Demo Bank.</summary>
    public const string AccountNumberPrefix = BankingMath.AccountNumberPrefix;

    private readonly IBankAccountRepository _accounts;
    private readonly ILedgerRepository _ledger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    /// <summary>Creates the account service.</summary>
    public AccountService(
        IBankAccountRepository accounts,
        ILedgerRepository ledger,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _accounts = accounts;
        _ledger = ledger;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>Returns every account of the user, the primary account first.</summary>
    public async Task<IReadOnlyList<AccountSummary>> GetAccountsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<BankAccount> accounts = await _accounts.GetForUserAsync(userId, cancellationToken);

        return accounts
            .OrderBy(account => account.Type == BankAccountType.Primary ? 0 : 1)
            .ThenBy(account => account.CreatedAtUtc)
            .Select(ToSummary)
            .ToList();
    }

    /// <summary>Returns the primary account of the user, or <c>null</c> when there is none yet.</summary>
    public async Task<AccountSummary?> GetPrimaryAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        BankAccount? account = await _accounts.GetPrimaryAsync(userId, cancellationToken);

        return account is null ? null : ToSummary(account);
    }

    /// <summary>Returns the sum of the balances of all accounts of the user.</summary>
    public async Task<Money> GetTotalBalanceAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<BankAccount> accounts = await _accounts.GetForUserAsync(userId, cancellationToken);

        return accounts.Aggregate(Money.Zero, (total, account) => total + account.Balance);
    }

    /// <summary>
    /// Returns the movements of the user, newest first.
    /// Only the entries of the accounts of the user are read, the balance of the other
    /// accounts is never touched.
    /// </summary>
    public async Task<IReadOnlyList<LedgerMovementItem>> GetHistoryAsync(
        Guid userId,
        int take,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<BankAccount> ownAccounts = await _accounts.GetForUserAsync(userId, cancellationToken);

        if (ownAccounts.Count == 0)
        {
            return [];
        }

        List<LedgerEntry> entries = [];

        foreach (BankAccount account in ownAccounts)
        {
            entries.AddRange(await _ledger.GetEntriesOfAccountAsync(account.Id, take, cancellationToken));
        }

        IReadOnlyList<LedgerTransfer> transfers = await _ledger.GetTransfersOfUserAsync(userId, take, cancellationToken);
        Dictionary<Guid, LedgerTransfer> transfersById = transfers.ToDictionary(transfer => transfer.Id);

        HashSet<Guid> ownAccountIds = ownAccounts.Select(account => account.Id).ToHashSet();
        HashSet<Guid> otherAccountIds = entries
            .Select(entry => transfersById.TryGetValue(entry.TransferId, out LedgerTransfer? transfer)
                ? transfer.FromAccountId == entry.AccountId ? transfer.ToAccountId : transfer.FromAccountId
                : Guid.Empty)
            .Where(id => id != Guid.Empty && !ownAccountIds.Contains(id))
            .ToHashSet();

        IReadOnlyList<BankAccount> counterparties = await _accounts.GetByIdsAsync(otherAccountIds, cancellationToken);
        Dictionary<Guid, string> names = counterparties.ToDictionary(account => account.Id, account => account.DisplayName);

        return entries
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .Take(take)
            .Select(entry =>
            {
                transfersById.TryGetValue(entry.TransferId, out LedgerTransfer? transfer);

                Guid otherId = transfer is null
                    ? Guid.Empty
                    : transfer.FromAccountId == entry.AccountId ? transfer.ToAccountId : transfer.FromAccountId;

                string counterparty = otherId != Guid.Empty && names.TryGetValue(otherId, out string? name)
                    ? name
                    : "BarsiG";

                return new LedgerMovementItem(
                    entry.TransferId,
                    transfer?.Kind ?? LedgerTransferKind.UserTransfer,
                    entry.Direction,
                    entry.Amount,
                    entry.BalanceAfter,
                    transfer?.Description ?? string.Empty,
                    counterparty,
                    entry.CreatedAtUtc);
            })
            .ToList();
    }

    /// <summary>
    /// Creates the primary account of a user when it does not exist yet.
    /// The account always starts with exactly zero, a new user never receives money.
    /// The changes are written by the caller, so a new customer and his first account
    /// can be stored in one single transaction.
    /// </summary>
    public async Task<Result<AccountSummary>> CreatePrimaryAccountAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        BankAccount? existing = await _accounts.GetPrimaryAsync(userId, cancellationToken);

        if (existing is not null)
        {
            return new Ok<AccountSummary>(ToSummary(existing));
        }

        IReadOnlyList<BankAccount> all = await _accounts.GetAllAsync(cancellationToken);
        long sequence = all.Count + 1;

        BankAccount account = new(
            Guid.NewGuid(),
            userId,
            BankingMath.BuildAccountNumber(BankingMath.AccountNumberPrefix, sequence),
            "Primary account",
            BankAccountType.Primary,
            BankAccountStatus.Active,
            Money.Zero,
            "KZT",
            0,
            _clock.UtcNow,
            null);

        await _accounts.AddAsync(account, cancellationToken);

        return new Ok<AccountSummary>(ToSummary(account));
    }

    /// <summary>
    /// Creates the primary account of a user when it does not exist yet and saves it.
    /// The account always starts with exactly zero, a new user never receives money.
    /// </summary>
    public async Task<Result<AccountSummary>> EnsurePrimaryAccountAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        Result<AccountSummary> result = await CreatePrimaryAccountAsync(userId, cancellationToken);

        if (result.IsFailure())
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return result;
    }

    /// <summary>Builds the read model of an account.</summary>
    public static AccountSummary ToSummary(BankAccount account) =>
        new(
            account.Id,
            account.AccountNumber,
            account.DisplayName,
            account.Type,
            account.Status,
            account.Balance,
            account.Currency,
            account.Type == BankAccountType.Primary);
}
