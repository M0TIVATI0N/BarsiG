using FinanceFlow.Domain.Common;

namespace FinanceFlow.UnitTests;

public sealed class MoneyTests
{
    [Fact]
    public void FromDecimal_StoresTheAmountInCents()
    {
        Money money = Money.FromDecimal(12.34m);

        Assert.Equal(1234, money.Cents);
    }

    [Fact]
    public void FromDecimal_RoundsHalfToEven()
    {
        Assert.Equal(10, Money.FromDecimal(0.1m).Cents);
        Assert.Equal(11, Money.FromDecimal(0.11m).Cents);
    }

    [Fact]
    public void ToDecimal_ReturnsTheAmountInTheMainUnit()
    {
        Assert.Equal(12.34m, Money.FromDecimal(12.34m).ToDecimal());
    }

    [Fact]
    public void Addition_AddsTheCents()
    {
        Money result = Money.FromDecimal(10.10m) + Money.FromDecimal(0.90m);

        Assert.Equal(1100, result.Cents);
    }

    [Fact]
    public void Subtraction_CanProduceANegativeAmount()
    {
        Money result = Money.FromDecimal(5m) - Money.FromDecimal(8m);

        Assert.True(result.IsNegative);
        Assert.Equal(-300, result.Cents);
    }

    [Fact]
    public void Multiplication_ScalesTheAmount()
    {
        Assert.Equal(100_000, (Money.FromDecimal(10m) * 100).Cents);
        Assert.Equal(500, (Money.FromDecimal(10m) * 0.5m).Cents);
    }

    [Fact]
    public void Comparison_WorksWithTheOperators()
    {
        Assert.True(Money.FromDecimal(10m) > Money.FromDecimal(9.99m));
        Assert.True(Money.FromDecimal(10m) >= Money.FromDecimal(10m));
        Assert.True(Money.FromDecimal(10m) <= Money.FromDecimal(10m));
        Assert.False(Money.FromDecimal(10m) < Money.FromDecimal(10m));
    }

    [Fact]
    public void Format_UsesTheSpaceAndTheCommaSeparators()
    {
        string formatted = Money.FromDecimal(1234567.89m).Format();

        Assert.Equal("1 234 567,89 ₸", formatted);
    }

    [Fact]
    public void Format_SupportsAnotherCurrencySymbol()
    {
        Assert.EndsWith("USD", Money.FromDecimal(1m).Format("USD"));
    }

    [Fact]
    public void IsZeroAndIsPositive_DescribeTheAmount()
    {
        Assert.True(Money.Zero.IsZero);
        Assert.True(Money.FromDecimal(0.01m).IsPositive);
        Assert.True(Money.FromDecimal(-0.01m).IsNegative);
    }

    [Fact]
    public void ExplicitConversions_WorkBothWays()
    {
        Money money = (Money)19.99m;
        decimal value = (decimal)money;

        Assert.Equal(1999, money.Cents);
        Assert.Equal(19.99m, value);
    }
}
