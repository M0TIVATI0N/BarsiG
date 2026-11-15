using System.Globalization;

namespace FinanceFlow.Domain.Common;

/// <summary>
/// An immutable monetary amount stored in the smallest currency unit (fractions of a unit).
/// Using an integer number of minor units removes any floating point rounding error,
/// which is essential for a financial application.
/// </summary>
public readonly record struct Money(long Cents)
{
    /// <summary>The default currency symbol used by the interface.</summary>
    public const string DefaultCurrencySymbol = "₸";

    /// <summary>An amount of zero.</summary>
    public static Money Zero => new(0);

    /// <summary>Creates an amount from a decimal value, rounding half to even.</summary>
    public static Money FromDecimal(decimal amount) =>
        new((long)Math.Round(amount * 100m, MidpointRounding.ToEven));

    /// <summary>Converts the amount back to a decimal value.</summary>
    public decimal ToDecimal() => Cents / 100m;

    /// <summary>Returns <c>true</c> when the amount is exactly zero.</summary>
    public bool IsZero => Cents == 0;

    /// <summary>Returns <c>true</c> when the amount is greater than zero.</summary>
    public bool IsPositive => Cents > 0;

    /// <summary>Returns <c>true</c> when the amount is less than zero.</summary>
    public bool IsNegative => Cents < 0;

    /// <summary>The absolute value of the amount.</summary>
    public Money Abs() => new(Math.Abs(Cents));

    /// <summary>
    /// Formats the amount with a space thousands separator and a comma decimal separator,
    /// followed by the currency symbol.
    /// </summary>
    public string Format(string currencySymbol = DefaultCurrencySymbol) =>
        $"{ToDecimal().ToString("N2", CultureInfo.InvariantCulture).Replace(",", " ").Replace(".", ",")} {currencySymbol}";

    public override string ToString() => Format();

    public static Money operator +(Money left, Money right) => new(left.Cents + right.Cents);

    public static Money operator -(Money left, Money right) => new(left.Cents - right.Cents);

    public static Money operator -(Money value) => new(-value.Cents);

    public static Money operator *(Money value, int factor) => new(value.Cents * factor);

    public static Money operator *(Money value, decimal factor) =>
        new((long)Math.Round(value.Cents * factor, MidpointRounding.ToEven));

    public static Money operator /(Money value, int divisor) => new(value.Cents / divisor);

    public static bool operator <(Money left, Money right) => left.Cents < right.Cents;

    public static bool operator >(Money left, Money right) => left.Cents > right.Cents;

    public static bool operator <=(Money left, Money right) => left.Cents <= right.Cents;

    public static bool operator >=(Money left, Money right) => left.Cents >= right.Cents;

    public static explicit operator decimal(Money value) => value.ToDecimal();

    public static explicit operator Money(decimal value) => FromDecimal(value);
}
