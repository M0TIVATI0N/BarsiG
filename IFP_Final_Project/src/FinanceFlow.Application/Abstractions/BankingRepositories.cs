using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Common;

namespace FinanceFlow.Application.Abstractions;

/// <summary>Stores and reads the security profile of a user.</summary>
public interface ISecurityProfileRepository
{
    /// <summary>Reads the profile of a user.</summary>
    Task<UserSecurityProfile?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Adds a new profile.</summary>
    Task AddAsync(UserSecurityProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Writes the changed profile back.</summary>
    Task UpdateAsync(UserSecurityProfile profile, CancellationToken cancellationToken = default);
}

/// <summary>Stores and reads the bank accounts.</summary>
public interface IBankAccountRepository
{
    /// <summary>Reads one account by its identifier.</summary>
    Task<BankAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Reads one account, but only if the given user owns it.</summary>
    Task<BankAccount?> GetOwnedAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Reads the primary account of a user.</summary>
    Task<BankAccount?> GetPrimaryAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Reads every account of a user, oldest first.</summary>
    Task<IReadOnlyList<BankAccount>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Reads every account of every user. Only the administration uses it.</summary>
    Task<IReadOnlyList<BankAccount>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the accounts with the given identifiers.
    /// It is used to resolve the name of the other side of a transfer without reading
    /// the balances of the accounts that belong to somebody else.
    /// </summary>
    Task<IReadOnlyList<BankAccount>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the reserve account of the Demo Bank.</summary>
    Task<BankAccount?> GetReserveAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a new account.</summary>
    Task AddAsync(BankAccount account, CancellationToken cancellationToken = default);

    /// <summary>Writes the changed account back.</summary>
    Task UpdateAsync(BankAccount account, CancellationToken cancellationToken = default);
}

/// <summary>Stores and reads the double entry ledger.</summary>
public interface ILedgerRepository
{
    /// <summary>Reads one transfer.</summary>
    Task<LedgerTransfer?> GetTransferAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Reads the transfer that was created for an idempotency key, if there is one.</summary>
    Task<LedgerTransfer?> FindByIdempotencyKeyAsync(
        Guid requestedByUserId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the transfers of a user, newest first.</summary>
    Task<IReadOnlyList<LedgerTransfer>> GetTransfersOfUserAsync(
        Guid userId,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>Calculates completed user transfers in a half-open UTC time range.</summary>
    Task<Money> GetCompletedUserTransferTotalAsync(
        Guid userId,
        DateTimeOffset fromUtcInclusive,
        DateTimeOffset toUtcExclusive,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the ledger entries of one account, newest first.</summary>
    Task<IReadOnlyList<LedgerEntry>> GetEntriesOfAccountAsync(
        Guid accountId,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates the balance of an account from its ledger entries alone.
    /// It is the independent check of the stored balance.
    /// </summary>
    Task<Money> ComputeBalanceAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates the balance of every account from the ledger entries alone.
    /// The administration uses it to prove that the ledger and the stored balances agree.
    /// </summary>
    Task<IReadOnlyList<LedgerAccountBalance>> ComputeAllBalancesAsync(CancellationToken cancellationToken = default);

    /// <summary>Counts the committed transfers of the whole bank.</summary>
    Task<int> CountTransfersAsync(CancellationToken cancellationToken = default);
}

/// <summary>The balance of one account, calculated from the ledger entries.</summary>
/// <param name="AccountId">The account.</param>
/// <param name="ComputedBalance">The balance that the ledger entries produce.</param>
/// <param name="EntryCount">The number of entries of the account.</param>
public sealed record LedgerAccountBalance(Guid AccountId, Money ComputedBalance, long EntryCount);

/// <summary>The request of one atomic money movement.</summary>
/// <param name="Kind">The business reason of the movement.</param>
/// <param name="FromAccountId">The account the money leaves.</param>
/// <param name="ToAccountId">The account the money arrives at.</param>
/// <param name="RequestedByUserId">The user who asked for the movement.</param>
/// <param name="Amount">The amount, always positive.</param>
/// <param name="Description">The description shown in the history.</param>
/// <param name="IdempotencyKey">The key that protects a repeated request.</param>
public sealed record LedgerExecutionRequest(
    LedgerTransferKind Kind,
    Guid FromAccountId,
    Guid ToAccountId,
    Guid RequestedByUserId,
    Money Amount,
    string Description,
    string IdempotencyKey,
    string? ExpectedRecipientPhoneNumber = null);

/// <summary>
/// Moves virtual money inside one database transaction.
/// The implementation is the only place that writes to the ledger, which guarantees that
/// a movement either happens completely or does not happen at all.
/// </summary>
public interface ILedgerExecutor
{
    /// <summary>
    /// Executes a money movement. The transfer, the two ledger entries and the two balances
    /// are written in a single transaction.
    /// </summary>
    /// <param name="request">The movement to execute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Result<LedgerTransfer>> ExecuteAsync(
        LedgerExecutionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>One possible recipient of a transfer, as it is shown to the user.</summary>
/// <param name="UserId">The identifier of the recipient.</param>
/// <param name="DisplayName">The display name of the recipient.</param>
/// <param name="MaskedEmail">The e-mail address of the recipient, hidden in the middle.</param>
public sealed record RecipientSuggestion(Guid UserId, string DisplayName, string MaskedEmail);

/// <summary>Finds the users that may receive a transfer.</summary>
public interface IUserDirectory
{
    /// <summary>Searches the directory by a part of the name or of the e-mail address.</summary>
    Task<IReadOnlyList<RecipientSuggestion>> SearchAsync(
        string query,
        Guid excludeUserId,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>Finds one recipient by the exact e-mail address.</summary>
    Task<RecipientSuggestion?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>Finds one recipient by the identifier of the user.</summary>
    Task<RecipientSuggestion?> FindByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Finds a recipient only when the exact phone identity is verified and eligible.</summary>
    Task<RecipientSuggestion?> FindByVerifiedPhoneNumberAsync(
        string canonicalPhoneNumber,
        bool allowLocalDemo,
        CancellationToken cancellationToken = default);
}
