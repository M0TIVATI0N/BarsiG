using FinanceFlow.Application.Mapping;
using FinanceFlow.Domain.Analytics;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Entities;
using FinanceFlow.Domain.Enums;

namespace FinanceFlow.Application.Services;

/// <summary>
/// Builds the analytical context out of already loaded data.
/// It is a pure function: the same data always produces the same context,
/// which makes the dashboards, the forecast and the insights consistent with each other.
/// </summary>
public static class AnalyticsContextBuilder
{
    /// <summary>The number of months shown in the historical charts.</summary>
    public const int HistoryMonths = 6;

    /// <summary>The number of days shown in the daily expense chart.</summary>
    public const int DailyWindow = 30;

    /// <summary>Builds the insight context.</summary>
    public static InsightContext Build(
        IReadOnlyList<Transaction> transactions,
        IReadOnlyList<Category> categories,
        IReadOnlyList<Budget> budgets,
        IReadOnlyList<SavingsGoal> goals,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(budgets);
        ArgumentNullException.ThrowIfNull(goals);

        DateOnly historyStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-(HistoryMonths - 1));

        IReadOnlyList<MonthlyPoint> history = AnalyticsCalculator.MonthlySeries(
            transactions,
            historyStart,
            HistoryMonths);

        IReadOnlyList<CategoryBreakdown> breakdown = AnalyticsCalculator.BreakdownByCategory(
            transactions,
            categories,
            TransactionKind.Expense);

        IReadOnlyList<TrendPoint> daily = AnalyticsCalculator.DailyExpenses(transactions, DailyWindow);

        IReadOnlyList<BudgetStatus> budgetStatuses = BuildBudgetStatuses(budgets, categories, transactions);

        IReadOnlyList<GoalProgress> goalProgress = goals
            .Where(goal => goal.Status == GoalStatus.Active)
            .Select(EntityMapper.ToProgress)
            .ToList();

        int daysWithoutActivity = transactions.Count == 0
            ? 0
            : today.DayNumber - transactions.Max(transaction => transaction.OccurredOn).DayNumber;

        return new InsightContext(
            history,
            budgetStatuses,
            breakdown,
            daily,
            AnalyticsCalculator.NetBalance(transactions),
            goalProgress,
            daysWithoutActivity);
    }

    /// <summary>Builds the state of every budget for the given transactions.</summary>
    public static IReadOnlyList<BudgetStatus> BuildBudgetStatuses(
        IReadOnlyList<Budget> budgets,
        IReadOnlyList<Category> categories,
        IReadOnlyList<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(budgets);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(transactions);

        Dictionary<Guid, Category> categoryById = categories.ToDictionary(category => category.Id);

        return budgets
            .Select(budget =>
            {
                Money spent = new(transactions
                    .Where(transaction => transaction.CategoryId == budget.CategoryId)
                    .Sum(transaction => transaction.Amount.Cents));

                double ratio = budget.Limit.IsZero ? 0d : (double)spent.Cents / budget.Limit.Cents;

                categoryById.TryGetValue(budget.CategoryId, out Category? category);

                return new BudgetStatus(
                    budget.CategoryId,
                    category?.Name ?? "Unknown",
                    category?.ColorHex ?? "#94A3B8",
                    budget.Limit,
                    spent,
                    Math.Round(ratio, 4, MidpointRounding.ToEven),
                    AnalyticsCalculator.ClassifyBudgetUsage(budget.Limit, spent));
            })
            .ToList();
    }
}
