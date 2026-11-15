using FinanceFlow.Application.Dtos;
using FinanceFlow.Domain.Analytics;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Entities;
using FinanceFlow.Domain.Validation;

namespace FinanceFlow.Application.Mapping;

/// <summary>
/// Pure projections from the domain model to the read models of the interface layer.
/// Keeping them in one place makes the user interface independent of the storage model.
/// </summary>
public static class EntityMapper
{
    /// <summary>Projects a transaction with its category.</summary>
    public static TransactionItem ToItem(Transaction transaction, Category? category) =>
        new(
            transaction.Id,
            transaction.CategoryId,
            category?.Name ?? "Unknown",
            category?.ColorHex ?? "#94A3B8",
            category?.Icon ?? "help",
            transaction.Amount,
            transaction.Kind,
            transaction.OccurredOn,
            transaction.Note,
            transaction.IsRecurring);

    /// <summary>Projects a category with the usage of the current month.</summary>
    public static CategoryItem ToItem(Category category, Money spent, int transactionCount) =>
        new(
            category.Id,
            category.Name,
            category.Kind,
            category.ColorHex,
            category.Icon,
            category.IsArchived,
            spent,
            transactionCount);

    /// <summary>Projects a budget with the category and the amount already spent.</summary>
    public static BudgetItem ToItem(Budget budget, Category? category, Money spent)
    {
        double ratio = budget.Limit.IsZero ? 0d : (double)spent.Cents / budget.Limit.Cents;

        return new BudgetItem(
            budget.Id,
            budget.CategoryId,
            category?.Name ?? "Unknown",
            category?.ColorHex ?? "#94A3B8",
            budget.Limit,
            spent,
            Math.Round(ratio, 4, MidpointRounding.ToEven),
            AnalyticsCalculator.ClassifyBudgetUsage(budget.Limit, spent),
            budget.Year,
            budget.Month);
    }

    /// <summary>Projects a savings goal and computes the remaining days.</summary>
    public static GoalItem ToItem(SavingsGoal goal, DateOnly today)
    {
        double ratio = goal.TargetAmount.IsZero ? 0d : (double)goal.SavedAmount.Cents / goal.TargetAmount.Cents;
        int? daysLeft = goal.TargetDate is null
            ? null
            : goal.TargetDate.Value.DayNumber - today.DayNumber;

        return new GoalItem(
            goal.Id,
            goal.Title,
            goal.TargetAmount,
            goal.SavedAmount,
            Math.Round(ratio, 4, MidpointRounding.ToEven),
            goal.TargetDate,
            goal.Status,
            goal.Note,
            daysLeft);
    }

    /// <summary>Projects a savings goal into the progress model used by the insight engine.</summary>
    public static GoalProgress ToProgress(SavingsGoal goal)
    {
        double ratio = goal.TargetAmount.IsZero ? 0d : (double)goal.SavedAmount.Cents / goal.TargetAmount.Cents;
        return new GoalProgress(goal.Title, goal.TargetAmount, goal.SavedAmount, ratio);
    }
}
