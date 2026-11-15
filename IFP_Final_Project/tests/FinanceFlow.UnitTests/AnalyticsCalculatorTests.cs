using FinanceFlow.Domain.Analytics;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Entities;
using FinanceFlow.Domain.Enums;
using FinanceFlow.Domain.Validation;

namespace FinanceFlow.UnitTests;

public sealed class AnalyticsCalculatorTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Transaction Create(
        Guid categoryId,
        TransactionKind kind,
        decimal amount,
        int year = 2026,
        int month = 3,
        int day = 10) =>
        new(
            Guid.NewGuid(),
            UserId,
            categoryId,
            Money.FromDecimal(amount),
            kind,
            new DateOnly(year, month, day),
            null,
            false,
            DateTimeOffset.UtcNow);

    [Fact]
    public void TotalIncome_OnlyCountsTheIncome()
    {
        Guid category = Guid.NewGuid();

        List<Transaction> transactions =
        [
            Create(category, TransactionKind.Income, 100m),
            Create(category, TransactionKind.Expense, 40m)
        ];

        Assert.Equal(10_000, AnalyticsCalculator.TotalIncome(transactions).Cents);
        Assert.Equal(4_000, AnalyticsCalculator.TotalExpense(transactions).Cents);
        Assert.Equal(6_000, AnalyticsCalculator.NetBalance(transactions).Cents);
    }

    [Fact]
    public void MonthlySeries_ContainsEveryRequestedMonth()
    {
        Guid category = Guid.NewGuid();

        List<Transaction> transactions =
        [
            Create(category, TransactionKind.Income, 100m, 2026, 1, 5),
            Create(category, TransactionKind.Expense, 30m, 2026, 3, 7)
        ];

        IReadOnlyList<MonthlyPoint> series = AnalyticsCalculator.MonthlySeries(
            transactions,
            new DateOnly(2026, 1, 1),
            4);

        Assert.Equal(4, series.Count);
        Assert.Equal("2026-01", series[0].Month);
        Assert.Equal(10_000, series[0].Income.Cents);
        Assert.Equal(0, series[1].Expense.Cents);
        Assert.Equal(3_000, series[2].Expense.Cents);
        Assert.Equal(-3_000, series[2].Net.Cents);
    }

    [Fact]
    public void BreakdownByCategory_CalculatesTheShare()
    {
        Guid food = Guid.NewGuid();
        Guid rent = Guid.NewGuid();

        List<Category> categories =
        [
            new(food, UserId, "Food", TransactionKind.Expense, "#FF0000", "cart", false, DateTimeOffset.UtcNow),
            new(rent, UserId, "Rent", TransactionKind.Expense, "#00FF00", "home", false, DateTimeOffset.UtcNow)
        ];

        List<Transaction> transactions =
        [
            Create(food, TransactionKind.Expense, 75m),
            Create(rent, TransactionKind.Expense, 25m)
        ];

        IReadOnlyList<CategoryBreakdown> breakdown = AnalyticsCalculator.BreakdownByCategory(
            transactions,
            categories,
            TransactionKind.Expense);

        Assert.Equal(2, breakdown.Count);
        Assert.Equal("Food", breakdown[0].Name);
        Assert.Equal(0.75d, breakdown[0].Share, 3);
        Assert.Equal(1d, breakdown.Sum(item => item.Share), 3);
    }

    [Fact]
    public void CumulativeBalance_AddsTheDaysOneByOne()
    {
        Guid category = Guid.NewGuid();

        List<Transaction> transactions =
        [
            Create(category, TransactionKind.Income, 100m, 2026, 3, 1),
            Create(category, TransactionKind.Expense, 30m, 2026, 3, 2),
            Create(category, TransactionKind.Expense, 20m, 2026, 3, 5)
        ];

        IReadOnlyList<TrendPoint> line = AnalyticsCalculator.CumulativeBalance(transactions);

        Assert.Equal(3, line.Count);
        Assert.Equal(100m, line[0].Value);
        Assert.Equal(70m, line[1].Value);
        Assert.Equal(50m, line[2].Value);
    }

    [Fact]
    public void SavingsRate_CalculatesTheShareOfTheIncome()
    {
        Assert.Equal(0.25m, AnalyticsCalculator.SavingsRate(Money.FromDecimal(100m), Money.FromDecimal(75m)));
        Assert.Equal(0m, AnalyticsCalculator.SavingsRate(Money.Zero, Money.FromDecimal(75m)));
    }

    [Fact]
    public void AverageTransactionAmount_DividesByTheNumberOfTransactions()
    {
        Guid category = Guid.NewGuid();

        List<Transaction> transactions =
        [
            Create(category, TransactionKind.Expense, 10m),
            Create(category, TransactionKind.Expense, 30m)
        ];

        Assert.Equal(20m, AnalyticsCalculator.AverageTransactionAmount(transactions));
        Assert.Equal(0m, AnalyticsCalculator.AverageTransactionAmount([]));
    }

    [Fact]
    public void MostExpensiveDay_FindsTheLargestDay()
    {
        Guid category = Guid.NewGuid();

        List<Transaction> transactions =
        [
            Create(category, TransactionKind.Expense, 10m, 2026, 3, 1),
            Create(category, TransactionKind.Expense, 90m, 2026, 3, 2)
        ];

        Assert.Equal(new DateOnly(2026, 3, 2), AnalyticsCalculator.MostExpensiveDay(transactions));
    }

    [Theory]
    [InlineData(0, 0, BudgetUsage.NotStarted)]
    [InlineData(50, 100, BudgetUsage.OnTrack)]
    [InlineData(90, 100, BudgetUsage.AlmostReached)]
    [InlineData(100, 100, BudgetUsage.Exceeded)]
    [InlineData(120, 100, BudgetUsage.Exceeded)]
    public void ClassifyBudgetUsage_DescribesTheStateOfTheLimit(
        decimal spent,
        decimal limit,
        BudgetUsage expected)
    {
        Assert.Equal(expected, AnalyticsCalculator.ClassifyBudgetUsage(
            Money.FromDecimal(limit),
            Money.FromDecimal(spent)));
    }

    [Fact]
    public void DailyExpenses_ProducesOnePointPerDay()
    {
        Guid category = Guid.NewGuid();
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        List<Transaction> transactions = [Create(category, TransactionKind.Expense, 10m, today.Year, today.Month, today.Day)];

        IReadOnlyList<TrendPoint> daily = AnalyticsCalculator.DailyExpenses(transactions, 7);

        Assert.Equal(7, daily.Count);
        Assert.Equal(10m, daily[^1].Value);
    }
}
