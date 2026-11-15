using System.Globalization;

using FinanceFlow.Domain.Common;

namespace FinanceFlow.Domain.Banking;

/// <summary>
/// The rules that bound a money movement. The values come from the configuration of the server,
/// so an administrator can tighten them without touching the code.
/// </summary>
/// <param name="MinimumTransfer">The smallest amount that may be transferred.</param>
/// <param name="MaximumTransfer">The largest amount of a single transfer.</param>
/// <param name="MaximumDailyTransfer">The largest total amount of one day.</param>
public sealed record BankingLimits(
    Money MinimumTransfer,
    Money MaximumTransfer,
    Money MaximumDailyTransfer)
{
    /// <summary>The limits of the educational Demo Bank.</summary>
    public static BankingLimits Default { get; } = new(
        Money.FromDecimal(1m),
        Money.FromDecimal(1_000_000m),
        Money.FromDecimal(2_000_000m));
}

/// <summary>
/// Pure functions of the banking module.
/// Every function is total, deterministic and free of side effects, so the rules of the
/// simulated bank can be proved with plain unit tests.
/// </summary>
public static class BankingMath
{
    /// <summary>The prefix of every account number of the Demo Bank.</summary>
    public const string AccountNumberPrefix = "KZ00";

    /// <summary>The number of months a year, used by the monthly interest.</summary>
    public const int MonthsPerYear = 12;

    /// <summary>
    /// The monthly interest of a deposit: <c>principal × rate / 12</c>.
    /// The result is rounded to whole cents, half to even, so the calculation is deterministic
    /// and never produces a fraction of a cent.
    /// </summary>
    /// <param name="principal">The balance of the deposit.</param>
    /// <param name="annualRatePercent">The yearly rate in percent, for example <c>12.0</c>.</param>
    public static Money CalculateMonthlyInterest(Money principal, decimal annualRatePercent)
    {
        if (principal.Cents <= 0 || annualRatePercent <= 0m)
        {
            return Money.Zero;
        }

        decimal yearlyInterest = principal.ToDecimal() * (annualRatePercent / 100m);
        decimal monthlyInterest = yearlyInterest / MonthsPerYear;

        return Money.FromDecimal(monthlyInterest);
    }

    /// <summary>
    /// The balance of a deposit after a number of months, when the interest is paid every
    /// month and the rate stays the same. The function is recursive: the balance of
    /// <paramref name="months"/> months is the balance of one month less plus the interest
    /// of that month. The definition is natural here, because every month is the same
    /// operation applied to the result of the month before.
    /// </summary>
    /// <param name="principal">The balance of the deposit today.</param>
    /// <param name="annualRatePercent">The yearly rate in percent.</param>
    /// <param name="months">The number of months of the projection.</param>
    public static Money ProjectBalance(Money principal, decimal annualRatePercent, int months)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(months);

        if (months == 0 || principal.Cents <= 0 || annualRatePercent <= 0m)
        {
            return principal;
        }

        decimal monthlyRate = annualRatePercent / 100m / MonthsPerYear;

        return ProjectBalance(principal + principal * monthlyRate, annualRatePercent, months - 1);
    }

    /// <summary>
    /// The interest of a deposit over several whole months.
    /// The interest is not compounded: every month is calculated from the same principal,
    /// which keeps the result reproducible.
    /// </summary>
    /// <param name="principal">The balance of the deposit.</param>
    /// <param name="annualRatePercent">The yearly rate in percent.</param>
    /// <param name="months">The number of months.</param>
    public static Money CalculateInterest(Money principal, decimal annualRatePercent, int months)
    {
        if (months <= 0)
        {
            return Money.Zero;
        }

        Money monthly = CalculateMonthlyInterest(principal, annualRatePercent);

        return monthly * months;
    }

    /// <summary>
    /// The signed value of a ledger entry. A debit is negative, a credit is positive.
    /// The sum of the signed values over all accounts is always zero.
    /// </summary>
    public static long SignedCents(LedgerEntryDirection direction, Money amount) =>
        direction == LedgerEntryDirection.Debit ? -amount.Cents : amount.Cents;

    /// <summary>
    /// Builds the visible account number of a new account.
    /// The number is only a label: it is unique, but it grants no access to the account.
    /// </summary>
    /// <param name="prefix">The country prefix, for example <c>KZ00</c>.</param>
    /// <param name="sequence">The serial number of the account.</param>
    public static string BuildAccountNumber(string prefix, long sequence)
    {
        string digits = Math.Abs(sequence).ToString("D12", CultureInfo.InvariantCulture);

        return $"{prefix} {digits[..4]} {digits[4..8]} {digits[8..]}";
    }

    /// <summary>
    /// Checks an amount against the limits of the bank.
    /// The function never throws, it explains what is wrong instead.
    /// </summary>
    /// <param name="amount">The requested amount.</param>
    /// <param name="limits">The configured limits.</param>
    public static Result<Money> ValidateAmount(Money amount, BankingLimits limits)
    {
        if (amount.IsNegative)
        {
            return new Fail<Money>("The amount must not be negative.")
            {
                Code = "negative_amount"
            };
        }

        if (amount.IsZero)
        {
            return new Fail<Money>("The amount must be greater than zero.")
            {
                Code = "zero_amount"
            };
        }

        if (amount < limits.MinimumTransfer)
        {
            return new Fail<Money>($"The smallest possible transfer is {limits.MinimumTransfer.Format()}.")
            {
                Code = "below_minimum"
            };
        }

        if (amount > limits.MaximumTransfer)
        {
            return new Fail<Money>($"The largest possible transfer is {limits.MaximumTransfer.Format()}.")
            {
                Code = "above_maximum"
            };
        }

        return new Ok<Money>(amount);
    }

    /// <summary>
    /// Checks the total amount of one day against the daily limit.
    /// </summary>
    /// <param name="alreadyTransferred">The total amount already transferred today.</param>
    /// <param name="requested">The requested amount.</param>
    /// <param name="limits">The configured limits.</param>
    public static Result<Money> ValidateDailyLimit(
        Money alreadyTransferred,
        Money requested,
        BankingLimits limits)
    {
        Money total = alreadyTransferred + requested;

        if (total > limits.MaximumDailyTransfer)
        {
            return new Fail<Money>($"The daily limit of {limits.MaximumDailyTransfer.Format()} is reached.")
            {
                Code = "daily_limit_exceeded"
            };
        }

        return new Ok<Money>(total);
    }
}
