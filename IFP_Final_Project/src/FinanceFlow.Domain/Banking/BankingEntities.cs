using FinanceFlow.Domain.Common;

namespace FinanceFlow.Domain.Banking;

/// <summary>The kind of a bank account.</summary>
public enum BankAccountType
{
    /// <summary>The main current account of a user. Every user owns exactly one.</summary>
    Primary = 0,

    /// <summary>A savings deposit opened by a user.</summary>
    Deposit = 1,

    /// <summary>The reserve of the educational Demo Bank. It belongs to nobody.</summary>
    Reserve = 2,

    /// <summary>
    /// The account that holds the money the bank issued. Its balance is negative,
    /// it is the counterpart of every virtual coin that exists inside the simulation.
    /// </summary>
    Issuance = 3
}

/// <summary>The lifecycle state of a bank account.</summary>
public enum BankAccountStatus
{
    /// <summary>The account can be used.</summary>
    Active = 0,

    /// <summary>The account is temporarily blocked and cannot receive or send money.</summary>
    Frozen = 1,

    /// <summary>The account was closed and keeps its history for ever.</summary>
    Closed = 2
}

/// <summary>The business reason of a ledger transfer.</summary>
public enum LedgerTransferKind
{
    /// <summary>A transfer from the primary account of one user to the primary account of another user.</summary>
    UserTransfer = 0,

    /// <summary>A transfer from the reserve of the Demo Bank to the primary account of a user.</summary>
    AdminFunding = 1,

    /// <summary>A transfer from a primary account to one of its own deposits.</summary>
    DepositContribution = 2,

    /// <summary>A transfer from a deposit back to the primary account of its owner.</summary>
    DepositWithdrawal = 3,

    /// <summary>The monthly interest of a deposit. It is funded from the reserve.</summary>
    InterestAccrual = 4
}

/// <summary>The state of a ledger transfer.</summary>
public enum LedgerTransferStatus
{
    /// <summary>The transfer was committed. Only this state creates ledger entries.</summary>
    Completed = 0,

    /// <summary>The transfer was rejected. It never changed a balance.</summary>
    Failed = 1
}

/// <summary>The direction of a ledger entry, seen from the account.</summary>
public enum LedgerEntryDirection
{
    /// <summary>The money leaves the account, the balance decreases.</summary>
    Debit = 0,

    /// <summary>The money arrives at the account, the balance increases.</summary>
    Credit = 1
}

/// <summary>
/// A container of virtual money.
/// The balance is never edited by the interface: it is written by the ledger executor
/// in the same database transaction as the corresponding ledger entries.
/// </summary>
/// <param name="Id">The technical identifier.</param>
/// <param name="UserId">The owner of the account, or <c>null</c> for a system account.</param>
/// <param name="AccountNumber">The visible account number, for example <c>KZ00 0000 0000 0042</c>.</param>
/// <param name="DisplayName">The name shown to the owner.</param>
/// <param name="Type">The kind of the account.</param>
/// <param name="Status">The lifecycle state of the account.</param>
/// <param name="Balance">The current balance in virtual KZT.</param>
/// <param name="Currency">The ISO code of the currency, always <c>KZT</c>.</param>
/// <param name="Version">The optimistic concurrency token of the account row.</param>
/// <param name="CreatedAtUtc">The moment of the opening.</param>
/// <param name="ClosedAtUtc">The moment of the closing, if the account was closed.</param>
public sealed record BankAccount(
    Guid Id,
    Guid? UserId,
    string AccountNumber,
    string DisplayName,
    BankAccountType Type,
    BankAccountStatus Status,
    Money Balance,
    string Currency,
    int Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClosedAtUtc);

/// <summary>
/// One business transaction of the bank. It is the head of a pair of ledger entries.
/// The record is never modified after it was committed: a correction is a compensating transfer.
/// </summary>
/// <param name="Id">The technical identifier, also shown on the receipt.</param>
/// <param name="IdempotencyKey">The key that makes a repeated request safe.</param>
/// <param name="Kind">The business reason of the transfer.</param>
/// <param name="Status">The state of the transfer.</param>
/// <param name="FromAccountId">The account the money leaves.</param>
/// <param name="ToAccountId">The account the money arrives at.</param>
/// <param name="RequestedByUserId">The user who asked for the transfer, or <c>null</c> for a system movement.</param>
/// <param name="Amount">The transferred amount, always positive.</param>
/// <param name="Currency">The ISO code of the currency, always <c>KZT</c>.</param>
/// <param name="Description">A short description shown in the history.</param>
/// <param name="FailureReason">Why the transfer was rejected, if it was rejected.</param>
/// <param name="CreatedAtUtc">The moment of the request.</param>
/// <param name="CompletedAtUtc">The moment of the commit.</param>
public sealed record LedgerTransfer(
    Guid Id,
    string IdempotencyKey,
    LedgerTransferKind Kind,
    LedgerTransferStatus Status,
    Guid FromAccountId,
    Guid ToAccountId,
    Guid? RequestedByUserId,
    Money Amount,
    string Currency,
    string Description,
    string? FailureReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

/// <summary>
/// One immutable line of the double entry ledger.
/// The signed sum of the entries of an account is always equal to its balance,
/// and the signed sum over all accounts is always zero.
/// </summary>
/// <param name="Id">The technical identifier.</param>
/// <param name="TransferId">The transfer this entry belongs to.</param>
/// <param name="AccountId">The account this entry belongs to.</param>
/// <param name="Direction">Whether the money leaves or arrives at the account.</param>
/// <param name="Amount">The magnitude of the movement, always positive.</param>
/// <param name="BalanceAfter">The balance of the account right after this entry was applied.</param>
/// <param name="CreatedAtUtc">The moment of the commit.</param>
public sealed record LedgerEntry(
    Guid Id,
    Guid TransferId,
    Guid AccountId,
    LedgerEntryDirection Direction,
    Money Amount,
    Money BalanceAfter,
    DateTimeOffset CreatedAtUtc);
