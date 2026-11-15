using FinanceFlow.Domain.Analytics;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Enums;
using FinanceFlow.Domain.Validation;

namespace FinanceFlow.UnitTests;

public sealed class InsightEngineTests
{
    private static List<MonthlyPoint> CreateHistory(params (int Year, int Month, decimal Expense)[] months) =>
        months
            .Select(month => new MonthlyPoint(
                $"{month.Year:D4}-{month.Month:D2}",
                Money.FromDecimal(month.Expense * 2m),
                Money.FromDecimal(month.Expense),
                Money.FromDecimal(month.Expense)))
            .ToList();

    [Fact]
    public void AStrongGrowthOfTheExpensesIsReported()
    {
        InsightContext context = new(
            CreateHistory((2026, 1, 100m), (2026, 2, 200m)),
            [],
            [],
            [],
            Money.Zero,
            [],
            0);

        IReadOnlyList<Insight> insights = InsightEngine.Generate(context);

        Assert.Contains(insights, insight => insight.Kind == InsightKind.Trend && insight.Severity == InsightSeverity.Warning);
    }

    [Fact]
    public void ASmallChangeIsNotReported()
    {
        InsightContext context = new(
            CreateHistory((2026, 1, 100m), (2026, 2, 105m)),
            [],
            [],
            [],
            Money.Zero,
            [],
            0);

        IReadOnlyList<Insight> insights = InsightEngine.Generate(context);

        Assert.DoesNotContain(insights, insight => insight.Kind == InsightKind.Trend);
    }

    [Fact]
    public void AnExceededBudgetIsCritical()
    {
        InsightContext context = new(
            CreateHistory((2026, 1, 100m)),
            [new BudgetStatus(Guid.NewGuid(), "Food", "#FF0000", Money.FromDecimal(100m), Money.FromDecimal(150m), 1.5d, BudgetUsage.Exceeded)],
            [],
            [],
            Money.Zero,
            [],
            0);

        IReadOnlyList<Insight> insights = InsightEngine.Generate(context);

        Insight insight = Assert.Single(insights.Where(item => item.Kind == InsightKind.Budget).ToList());

        Assert.Equal(InsightSeverity.Critical, insight.Severity);
    }

    [Fact]
    public void AnAlmostReachedBudgetIsAWarning()
    {
        InsightContext context = new(
            CreateHistory((2026, 1, 100m)),
            [new BudgetStatus(Guid.NewGuid(), "Food", "#FF0000", Money.FromDecimal(100m), Money.FromDecimal(90m), 0.9d, BudgetUsage.AlmostReached)],
            [],
            [],
            Money.Zero,
            [],
            0);

        IReadOnlyList<Insight> insights = InsightEngine.Generate(context);

        Assert.Contains(insights, insight => insight.Severity == InsightSeverity.Warning);
    }

    [Fact]
    public void ADominantCategoryIsReported()
    {
        InsightContext context = new(
            CreateHistory((2026, 1, 100m)),
            [],
            [new CategoryBreakdown(Guid.NewGuid(), "Rent", "#FF0000", "home", Money.FromDecimal(80m), 0.8d)],
            [],
            Money.Zero,
            [],
            0);

        IReadOnlyList<Insight> insights = InsightEngine.Generate(context);

        Assert.Contains(insights, insight => insight.Kind == InsightKind.Distribution);
    }

    [Fact]
    public void AGoalThatIsAlmostDoneIsReported()
    {
        InsightContext context = new(
            CreateHistory((2026, 1, 100m)),
            [],
            [],
            [],
            Money.Zero,
            [new GoalProgress("Laptop", Money.FromDecimal(100m), Money.FromDecimal(85m), 0.85d)],
            0);

        IReadOnlyList<Insight> insights = InsightEngine.Generate(context);

        Assert.Contains(insights, insight => insight.Kind == InsightKind.Goal);
    }

    [Fact]
    public void ALongPeriodWithoutActivityIsReported()
    {
        InsightContext context = new(
            CreateHistory((2026, 1, 100m)),
            [],
            [],
            [],
            Money.Zero,
            [],
            12);

        IReadOnlyList<Insight> insights = InsightEngine.Generate(context);

        Assert.Contains(insights, insight => insight.Title.Contains("No transactions"));
    }

    [Fact]
    public void TheInsightsAreSortedBySeverity()
    {
        InsightContext context = new(
            CreateHistory((2026, 1, 100m), (2026, 2, 300m)),
            [new BudgetStatus(Guid.NewGuid(), "Food", "#FF0000", Money.FromDecimal(100m), Money.FromDecimal(150m), 1.5d, BudgetUsage.Exceeded)],
            [],
            [],
            Money.Zero,
            [],
            30);

        IReadOnlyList<Insight> insights = InsightEngine.Generate(context);

        Assert.True(insights.Count > 1);
        Assert.Equal(InsightSeverity.Critical, insights[0].Severity);
    }
}
