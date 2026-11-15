using FinanceFlow.Domain.Common;

namespace FinanceFlow.Domain.Banking;

/// <summary>The lifecycle state of a deposit.</summary>
public enum DepositStatus
{
    /// <summary>The deposit accepts money and earns interest.</summary>
    Active = 0,

    /// <summary>The deposit was closed. It keeps its history for ever.</summary>
    Closed = 1
}

/// <summary>
/// A savings deposit of a customer of the Demo Bank.
/// The money of a deposit lives on its own bank account, so every movement of the money
/// is a normal ledger transfer and the conservation of the money is kept.
/// </summary>
/// <param name="Id">The technical identifier.</param>
/// <param name="UserId">The owner of the deposit.</param>
/// <param name="BankAccountId">The account that holds the money of the deposit.</param>
/// <param name="Name">The name the customer gave to the deposit.</param>
/// <param name="AnnualRatePercent">The yearly interest rate in percent.</param>
/// <param name="TermMonths">The planned term of the deposit in months.</param>
/// <param name="Status">The lifecycle state of the deposit.</param>
/// <param name="LastAccruedMonth">The last month the interest was paid for.</param>
/// <param name="OpenedAtUtc">The moment of the opening.</param>
/// <param name="ClosedAtUtc">The moment of the closing, if the deposit was closed.</param>
public sealed record Deposit(
    Guid Id,
    Guid UserId,
    Guid BankAccountId,
    string Name,
    decimal AnnualRatePercent,
    int TermMonths,
    DepositStatus Status,
    DateOnly LastAccruedMonth,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset? ClosedAtUtc);

/// <summary>
/// One monthly interest payment of a deposit.
/// The unique index on the pair of deposit and month makes a double accrual impossible.
/// </summary>
/// <param name="Id">The technical identifier.</param>
/// <param name="DepositId">The deposit the interest belongs to.</param>
/// <param name="PeriodMonth">The month the interest was paid for.</param>
/// <param name="Interest">The paid interest.</param>
/// <param name="BalanceAfter">The balance of the deposit after the payment.</param>
/// <param name="TransferId">The ledger transfer that moved the money.</param>
/// <param name="AccruedAtUtc">The moment of the payment.</param>
public sealed record DepositInterestAccrual(
    Guid Id,
    Guid DepositId,
    DateOnly PeriodMonth,
    Money Interest,
    Money BalanceAfter,
    Guid TransferId,
    DateTimeOffset AccruedAtUtc);
