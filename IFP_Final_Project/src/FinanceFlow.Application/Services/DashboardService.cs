using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Dtos;
using FinanceFlow.Application.Mapping;
using FinanceFlow.Domain.Analytics;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Entities;
using FinanceFlow.Domain.Enums;

namespace FinanceFlow.Application.Services;

/// <summary>
/// Builds the content of the dashboard.
/// All data is loaded exactly once and every number is derived by the pure functions
/// of the domain layer, so the page never runs a query per card.
/// </summary>
public sealed class DashboardService
{
    /// <summary>The number of transactions shown in the recent activity list.</summary>
    public const int RecentCount = 6;

    private readonly ITransactionRepository _transactions;
    private readonly ICategoryRepository _categories;
    private readonly IBudgetRepository _budgets;
    private readonly ISavingsGoalRepository _goals;
    private readonly IClock _clock;

    /// <summary>Creates the dashboard service.</summary>
    public DashboardService(
        ITransactionRepository transactions,
        ICategoryRepository categories,
        IBudgetRepository budgets,
        ISavingsGoalRepository goals,
        IClock clock)
    {
        _transactions = transactions;
        _categories = categories;
        _budgets = budgets;
        _goals = goals;
        _clock = clock;
    }

    /// <summary>Returns the dashboard of the user.</summary>
    public async Task<DashboardSummary> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        DateOnly today = _clock.Today;

        IReadOnlyList<Transaction> transactions = await _transactions.GetAllAsync(userId, cancellationToken);
        IReadOnlyList<Category> categories = await _categories.GetAllAsync(userId, true, cancellationToken);
        IReadOnlyList<Budget> budgets = await _budgets.GetAllAsync(userId, today.Year, today.Month, cancellationToken);
        IReadOnlyList<SavingsGoal> goals = await _goals.GetAllAsync(userId, cancellationToken);

        Dictionary<Guid, Category> categoryById = categories.ToDictionary(category => category.Id);

        Money income = AnalyticsCalculator.TotalIncome(transactions);
        Money expense = AnalyticsCalculator.TotalExpense(transactions);
        Money balance = income - expense;

        DateOnly monthStart = new(today.Year, today.Month, 1);
        Money spentThisMonth = new(transactions
            .Where(transaction => transaction.Kind == TransactionKind.Expense && transaction.OccurredOn >= monthStart)
            .Sum(transaction => transaction.Amount.Cents));

        InsightContext context = AnalyticsContextBuilder.Build(transactions, categories, budgets, goals, today);
        IReadOnlyList<Insight> insights = InsightEngine.Generate(context);

        IReadOnlyList<TransactionItem> recent = transactions
            .OrderByDescending(transaction => transaction.OccurredOn)
            .ThenByDescending(transaction => transaction.CreatedAtUtc)
            .Head(RecentCount)
            .Transform(transaction => EntityMapper.ToItem(transaction, categoryById.GetValueOrDefault(transaction.CategoryId)))
            .ToList();

        IReadOnlyList<GoalItem> goalItems = goals
            .Select(goal => EntityMapper.ToItem(goal, today))
            .OrderBy(goal => goal.Status)
            .ThenByDescending(goal => goal.Ratio)
            .ToList();

        return new DashboardSummary(
            balance,
            income,
            expense,
            AnalyticsCalculator.SavingsRate(income, expense),
            transactions.Count,
            spentThisMonth,
            context.History,
            context.ExpenseBreakdown,
            context.Budgets,
            insights,
            recent,
            goalItems);
    }
}
