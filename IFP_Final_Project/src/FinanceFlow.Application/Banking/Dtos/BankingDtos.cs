using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Common;

namespace FinanceFlow.Application.Banking.Dtos;

/// <summary>One bank account prepared for the interface layer.</summary>
/// <param name="Id">The identifier of the account.</param>
/// <param name="AccountNumber">The visible account number.</param>
/// <param name="DisplayName">The name of the account.</param>
/// <param name="Type">The kind of the account.</param>
/// <param name="Status">The lifecycle state of the account.</param>
/// <param name="Balance">The current balance.</param>
/// <param name="Currency">The ISO code of the currency.</param>
/// <param name="IsPrimary">Whether the account is the primary account of the user.</param>
public sealed record AccountSummary(
    Guid Id,
    string AccountNumber,
    string DisplayName,
    BankAccountType Type,
    BankAccountStatus Status,
    Money Balance,
    string Currency,
    bool IsPrimary);

/// <summary>One movement of the ledger, prepared for the interface layer.</summary>
/// <param name="TransferId">The identifier of the transfer.</param>
/// <param name="Kind">The business reason of the movement.</param>
/// <param name="Direction">Whether the money left or arrived at the account.</param>
/// <param name="Amount">The amount of the movement.</param>
/// <param name="BalanceAfter">The balance right after the movement.</param>
/// <param name="Description">The description of the movement.</param>
/// <param name="CounterpartyName">The name of the other side of the movement.</param>
/// <param name="OccurredAtUtc">The moment of the movement.</param>
public sealed record LedgerMovementItem(
    Guid TransferId,
    LedgerTransferKind Kind,
    LedgerEntryDirection Direction,
    Money Amount,
    Money BalanceAfter,
    string Description,
    string CounterpartyName,
    DateTimeOffset OccurredAtUtc);

/// <summary>The details of a transfer, shown before the user confirms it.</summary>
/// <param name="SenderName">The display name of the sender.</param>
/// <param name="SenderAccountNumber">The account number of the sender.</param>
/// <param name="SenderBalance">The balance of the sender before the transfer.</param>
/// <param name="RecipientName">The display name of the recipient.</param>
/// <param name="RecipientAccountNumber">The account number of the recipient.</param>
/// <param name="RecipientEmailHint">The hidden e-mail address of the recipient.</param>
/// <param name="Amount">The amount that will be moved.</param>
/// <param name="Currency">The ISO code of the currency.</param>
/// <param name="Description">The description of the transfer.</param>
/// <param name="BalanceAfter">The balance of the sender after the transfer.</param>
public sealed record TransferPreview(
    string SenderName,
    string SenderAccountNumber,
    Money SenderBalance,
    string RecipientName,
    string RecipientAccountNumber,
    string RecipientEmailHint,
    Money Amount,
    string Currency,
    string Description,
    Money BalanceAfter,
    string RecipientPhoneHint = "");

/// <summary>Safe details of a phone-verified transfer recipient.</summary>
/// <param name="UserId">The internal recipient identifier used only by the server-side flow.</param>
/// <param name="DisplayName">The recipient's safe display name.</param>
/// <param name="MaskedPhoneNumber">The recipient's masked verified phone number.</param>
/// <param name="DestinationAccountHint">A limited hint for the recipient's destination account.</param>
public sealed record PhoneRecipientPreview(
    Guid UserId,
    string DisplayName,
    string MaskedPhoneNumber,
    string DestinationAccountHint);

/// <summary>One deposit prepared for the interface layer.</summary>
/// <param name="Id">The identifier of the deposit.</param>
/// <param name="Name">The name of the deposit.</param>
/// <param name="Balance">The money that is inside the deposit.</param>
/// <param name="AnnualRatePercent">The yearly interest rate in percent.</param>
/// <param name="TermMonths">The planned term in months.</param>
/// <param name="Status">The lifecycle state of the deposit.</param>
/// <param name="LastAccruedMonth">The last month the interest was paid for.</param>
/// <param name="OpenedAtUtc">The moment of the opening.</param>
public sealed record DepositItem(
    Guid Id,
    string Name,
    Money Balance,
    decimal AnnualRatePercent,
    int TermMonths,
    DepositStatus Status,
    DateOnly LastAccruedMonth,
    DateTimeOffset OpenedAtUtc)
{
    /// <summary>
    /// The balance of the deposit at the end of its term, when the rate stays the same.
    /// The projection is a pure recursive function of the domain, it never touches the
    /// database and never moves any money.
    /// </summary>
    public Money ProjectedBalance => BankingMath.ProjectBalance(Balance, AnnualRatePercent, TermMonths);
}

/// <summary>One interest payment of a deposit.</summary>
/// <param name="PeriodMonth">The month the interest was paid for.</param>
/// <param name="Interest">The paid interest.</param>
/// <param name="BalanceAfter">The balance of the deposit after the payment.</param>
/// <param name="AccruedAtUtc">The moment of the payment.</param>
public sealed record DepositInterestItem(
    DateOnly PeriodMonth,
    Money Interest,
    Money BalanceAfter,
    DateTimeOffset AccruedAtUtc);

/// <summary>One virtual card prepared for the interface layer.</summary>
/// <param name="Id">The identifier of the card.</param>
/// <param name="Nickname">The name of the card.</param>
/// <param name="MaskedNumber">The number of the card, only the last four digits are visible.</param>
/// <param name="LastFour">The last four digits of the card.</param>
/// <param name="Status">The lifecycle state of the card.</param>
/// <param name="LinkedAccountId">The account the card belongs to.</param>
/// <param name="DailyLimit">The limit of one day.</param>
/// <param name="MonthlyLimit">The limit of one month.</param>
/// <param name="CreatedAtUtc">The moment of the creation.</param>
public sealed record VirtualCardItem(
    Guid Id,
    string Nickname,
    string MaskedNumber,
    string LastFour,
    VirtualCardStatus Status,
    Guid LinkedAccountId,
    Money DailyLimit,
    Money MonthlyLimit,
    DateTimeOffset CreatedAtUtc);

/// <summary>One customer of the Demo Bank.</summary>
/// <param name="UserId">The identifier of the customer.</param>
/// <param name="Name">The display name of the customer.</param>
/// <param name="Email">The e-mail address of the customer.</param>
/// <param name="MaskedEmail">The e-mail address of the customer, hidden in the middle.</param>
/// <param name="PrimaryAccountId">The primary account of the customer.</param>
/// <param name="Balance">The balance of the primary account.</param>
public sealed record AdminCustomerItem(
    Guid UserId,
    string Name,
    string Email,
    string MaskedEmail,
    Guid PrimaryAccountId,
    Money Balance);

/// <summary>The state of the Demo Bank, shown to the administrator.</summary>
/// <param name="ReserveBalance">The money that is left in the reserve.</param>
/// <param name="CustomerBalance">The money that the customers hold together.</param>
/// <param name="CustomerCount">The number of the customers.</param>
/// <param name="AccountCount">The number of the accounts.</param>
/// <param name="TransferCount">The number of the committed transfers.</param>
/// <param name="LedgerIsConsistent">Whether the stored balances agree with the ledger.</param>
public sealed record AdminOverview(
    Money ReserveBalance,
    Money CustomerBalance,
    int CustomerCount,
    int AccountCount,
    int TransferCount,
    bool LedgerIsConsistent);

/// <summary>The result of a funding of the Demo Bank.</summary>
/// <param name="TransferId">The identifier of the ledger transfer.</param>
/// <param name="Amount">The amount that was given to the customer.</param>
/// <param name="ReserveBalance">The reserve after the funding.</param>
/// <param name="CustomerBalance">The balance of the customer after the funding.</param>
/// <param name="CustomerName">The display name of the customer.</param>
public sealed record AdminFundingOutcome(
    Guid TransferId,
    Money Amount,
    Money ReserveBalance,
    Money CustomerBalance,
    string CustomerName);

/// <summary>The data of a transfer form.</summary>
/// <param name="RecipientUserId">The recipient of the money.</param>
/// <param name="Amount">The amount in KZT.</param>
/// <param name="Description">An optional description.</param>
/// <param name="IdempotencyKey">The key that makes a repeated submit safe.</param>
/// <param name="Pin">The transaction PIN, when the customer has to confirm the operation.</param>
/// <param name="Password">The password, when the session is too old for a sensitive operation.</param>
public sealed record TransferRequest(
    Guid RecipientUserId,
    decimal Amount,
    string? Description,
    string IdempotencyKey,
    string? Pin = null,
    string? Password = null,
    string? RecipientPhoneNumber = null);

/// <summary>The result of a completed transfer.</summary>
/// <param name="TransferId">The identifier of the transfer.</param>
/// <param name="Amount">The transferred amount.</param>
/// <param name="SenderBalance">The balance of the sender after the transfer.</param>
/// <param name="RecipientBalance">The balance of the recipient after the transfer.</param>
/// <param name="RecipientName">The display name of the recipient.</param>
/// <param name="CompletedAtUtc">The moment of the commit.</param>
public sealed record TransferOutcome(
    Guid TransferId,
    Money Amount,
    Money SenderBalance,
    Money RecipientBalance,
    string RecipientName,
    DateTimeOffset CompletedAtUtc);

/// <summary>The data of the receipt of one transfer.</summary>
/// <param name="TransferId">The identifier of the transfer.</param>
/// <param name="Kind">The business reason of the transfer.</param>
/// <param name="Status">The state of the transfer.</param>
/// <param name="Amount">The transferred amount.</param>
/// <param name="Currency">The ISO code of the currency.</param>
/// <param name="SenderName">The display name of the sender.</param>
/// <param name="RecipientName">The display name of the recipient.</param>
/// <param name="SenderAccountNumber">The masked account number of the sender.</param>
/// <param name="RecipientAccountNumber">The masked account number of the recipient.</param>
/// <param name="Description">The description of the transfer.</param>
/// <param name="CreatedAtUtc">The moment of the request.</param>
/// <param name="CompletedAtUtc">The moment of the commit.</param>
public sealed record TransferReceiptData(
    Guid TransferId,
    LedgerTransferKind Kind,
    LedgerTransferStatus Status,
    Money Amount,
    string Currency,
    string SenderName,
    string RecipientName,
    string SenderAccountNumber,
    string RecipientAccountNumber,
    string Description,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);
