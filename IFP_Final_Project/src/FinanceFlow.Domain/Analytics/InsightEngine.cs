using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Enums;
using FinanceFlow.Domain.Validation;

namespace FinanceFlow.Domain.Analytics;

/// <summary>
/// A rule based insight engine.
/// Every rule is a pure function of the <see cref="InsightContext"/>, the rules are independent
/// of each other and the resulting list is sorted by severity. New rules can be added without
/// touching the existing ones.
/// </summary>
public static class InsightEngine
{
    /// <summary>The relative change above which the engine reports a growing expense trend.</summary>
    public const double GrowthThreshold = 0.15d;

    /// <summary>The relative change below which the engine reports a shrinking expense trend.</summary>
    public const double ShrinkThreshold = 0.10d;

    /// <summary>A transaction is unusual when it is this many times larger than the average one.</summary>
    public const decimal AnomalyFactor = 3m;

    /// <summary>Generates all insights for the given context.</summary>
    public static IReadOnlyList<Insight> Generate(InsightContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<Insight> insights = [];
        insights.AddRange(ExpenseTrendInsights(context.History));
        insights.AddRange(BudgetInsights(context.Budgets));
        insights.AddRange(AnomalyInsights(context));
        insights.AddRange(DistributionInsights(context.ExpenseBreakdown));
        insights.AddRange(GoalInsights(context.Goals));
        insights.AddRange(ActivityInsights(context));

        return insights
            .OrderByDescending(insight => insight.Severity)
            .ThenBy(insight => insight.Kind)
            .ToList();
    }

    private static IEnumerable<Insight> ExpenseTrendInsights(IReadOnlyList<MonthlyPoint> history)
    {
        if (history.Count < 2)
        {
            yield break;
        }

        MonthlyPoint current = history[^1];
        MonthlyPoint previous = history[^2];

        if (previous.Expense.IsZero)
        {
            yield break;
        }

        double change = (double)(current.Expense.Cents - previous.Expense.Cents) / previous.Expense.Cents;

        if (change >= GrowthThreshold)
        {
            yield return new Insight(
                InsightKind.Trend,
                InsightSeverity.Warning,
                "Expenses are growing",
                $"Your expenses of {current.Month} are {change:P0} higher than in {previous.Month}.",
                "Review the categories that grew the most.");
        }
        else if (change <= -ShrinkThreshold)
        {
            yield return new Insight(
                InsightKind.Trend,
                InsightSeverity.Info,
                "You are spending less",
                $"Your expenses of {current.Month} are {Math.Abs(change):P0} lower than in {previous.Month}.",
                "Keep the current pace.");
        }
    }

    private static IEnumerable<Insight> BudgetInsights(IReadOnlyList<BudgetStatus> budgets)
    {
        foreach (BudgetStatus budget in budgets)
        {
            if (budget.Usage == BudgetUsage.Exceeded)
            {
                yield return new Insight(
                    InsightKind.Budget,
                    InsightSeverity.Critical,
                    $"Budget exceeded: {budget.CategoryName}",
                    $"You spent {budget.Spent.Format()} of the {budget.Limit.Format()} limit.",
                    "Reduce spending in this category or raise the limit.");
            }
            else if (budget.Usage == BudgetUsage.AlmostReached)
            {
                yield return new Insight(
                    InsightKind.Budget,
                    InsightSeverity.Warning,
                    $"Budget almost reached: {budget.CategoryName}",
                    $"You already used {budget.Ratio:P0} of the {budget.Limit.Format()} limit.",
                    "Only a little is left for the rest of the month.");
            }
        }
    }

    private static IEnumerable<Insight> AnomalyInsights(InsightContext context)
    {
        if (context.DailyExpenses.Count == 0)
        {
            yield break;
        }

        decimal average = context.DailyExpenses.Average(point => point.Value);
        if (average <= 0m)
        {
            yield break;
        }

        TrendPoint? peak = context.DailyExpenses
            .OrderByDescending(point => point.Value)
            .FirstOrDefault();

        if (peak is not null && peak.Value >= average * AnomalyFactor)
        {
            yield return new Insight(
                InsightKind.Anomaly,
                InsightSeverity.Warning,
                "Unusual spending day",
                $"On {peak.Label} you spent {Money.FromDecimal((decimal)peak.Value).Format()}, " +
                $"which is {peak.Value / average:P0} of your daily average.",
                "Check whether this was a one time purchase.");
        }
    }

    private static IEnumerable<Insight> DistributionInsights(IReadOnlyList<CategoryBreakdown> breakdown)
    {
        CategoryBreakdown? largest = breakdown.FirstOrDefault();
        if (largest is null || largest.Share < 0.5d)
        {
            yield break;
        }

        yield return new Insight(
            InsightKind.Distribution,
            InsightSeverity.Info,
            "One category dominates your spending",
            $"{largest.Name} accounts for {largest.Share:P0} of all expenses.",
            "A balanced budget usually spreads money over several categories.");
    }

    private static IEnumerable<Insight> GoalInsights(IReadOnlyList<GoalProgress> goals)
    {
        foreach (GoalProgress goal in goals.Where(goal => goal.Ratio >= 0.8d && goal.Ratio < 1d))
        {
            yield return new Insight(
                InsightKind.Goal,
                InsightSeverity.Info,
                $"Almost there: {goal.Title}",
                $"You saved {goal.Saved.Format()} of {goal.Target.Format()} ({goal.Ratio:P0}).",
                "A small final push completes this goal.");
        }
    }

    private static IEnumerable<Insight> ActivityInsights(InsightContext context)
    {
        if (context.DaysWithoutActivity >= 7)
        {
            yield return new Insight(
                InsightKind.Trend,
                InsightSeverity.Info,
                "No transactions for a while",
                $"The last recorded transaction is {context.DaysWithoutActivity} days old.",
                "Add the missing operations to keep the analytics accurate.");
        }
    }
}
