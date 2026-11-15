using FinanceFlow.Application.Abstractions;
using FinanceFlow.Application.Dtos;
using FinanceFlow.Domain.Analytics;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Entities;
using FinanceFlow.Domain.Enums;

namespace FinanceFlow.Application.Services;

/// <summary>Builds the analytical report of the transactions page.</summary>
public sealed class AnalyticsService
{
    private readonly ITransactionRepository _transactions;
    private readonly ICategoryRepository _categories;
    private readonly IClock _clock;

    /// <summary>Creates the analytics service.</summary>
    public AnalyticsService(
        ITransactionRepository transactions,
        ICategoryRepository categories,
        IClock clock)
    {
        _transactions = transactions;
        _categories = categories;
        _clock = clock;
    }

    /// <summary>Returns the analytics report for the last months.</summary>
    public async Task<AnalyticsReport> GetAsync(
        Guid userId,
        int months = 6,
        CancellationToken cancellationToken = default)
    {
        DateOnly today = _clock.Today;
        DateOnly from = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));

        IReadOnlyList<Transaction> transactions = await _transactions.GetRangeAsync(
            userId,
            from,
            today,
            cancellationToken);

        IReadOnlyList<Category> categories = await _categories.GetAllAsync(userId, true, cancellationToken);

        Money income = AnalyticsCalculator.TotalIncome(transactions);
        Money expense = AnalyticsCalculator.TotalExpense(transactions);

        return new AnalyticsReport(
            AnalyticsCalculator.MonthlySeries(transactions, from, months),
            AnalyticsCalculator.BreakdownByCategory(transactions, categories, TransactionKind.Expense),
            AnalyticsCalculator.BreakdownByCategory(transactions, categories, TransactionKind.Income),
            AnalyticsCalculator.CumulativeBalance(transactions),
            AnalyticsCalculator.DailyExpenses(transactions, AnalyticsContextBuilder.DailyWindow),
            AnalyticsCalculator.SavingsRate(income, expense),
            AnalyticsCalculator.AverageTransactionAmount(transactions),
            AnalyticsCalculator.AverageTransactionsPerMonth(transactions, months),
            AnalyticsCalculator.MostExpensiveDay(transactions),
            income,
            expense);
    }
}

/// <summary>Builds the forecast of the coming months.</summary>
public sealed class ForecastingService
{
    private readonly ITransactionRepository _transactions;
    private readonly IClock _clock;

    /// <summary>Creates the forecasting service.</summary>
    public ForecastingService(ITransactionRepository transactions, IClock clock)
    {
        _transactions = transactions;
        _clock = clock;
    }

    /// <summary>
    /// Returns the forecast of the next months calculated with two independent methods,
    /// so the user can compare a flexible trend with a stable average.
    /// </summary>
    public async Task<ForecastReport> GetAsync(
        Guid userId,
        int horizon = ForecastingEngine.DefaultHorizon,
        CancellationToken cancellationToken = default)
    {
        DateOnly today = _clock.Today;
        DateOnly from = new DateOnly(today.Year, today.Month, 1).AddMonths(-(AnalyticsContextBuilder.HistoryMonths - 1));

        IReadOnlyList<Transaction> transactions = await _transactions.GetRangeAsync(
            userId,
            from,
            today,
            cancellationToken);

        IReadOnlyList<MonthlyPoint> history = AnalyticsCalculator.MonthlySeries(
            transactions,
            from,
            AnalyticsContextBuilder.HistoryMonths);

        return new ForecastReport(
            history,
            ForecastingEngine.Forecast(history, horizon),
            ForecastingEngine.ForecastMovingAverage(history, horizon));
    }
}

/// <summary>Builds the list of smart insights for the user.</summary>
public sealed class InsightsService
{
    private readonly ITransactionRepository _transactions;
    private readonly ICategoryRepository _categories;
    private readonly IBudgetRepository _budgets;
    private readonly ISavingsGoalRepository _goals;
    private readonly IClock _clock;

    /// <summary>Creates the insights service.</summary>
    public InsightsService(
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

    /// <summary>Returns the insights of the user, most important one first.</summary>
    public async Task<IReadOnlyList<Insight>> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        DateOnly today = _clock.Today;

        IReadOnlyList<Transaction> transactions = await _transactions.GetAllAsync(userId, cancellationToken);
        IReadOnlyList<Category> categories = await _categories.GetAllAsync(userId, true, cancellationToken);
        IReadOnlyList<Budget> budgets = await _budgets.GetAllAsync(userId, today.Year, today.Month, cancellationToken);
        IReadOnlyList<SavingsGoal> goals = await _goals.GetAllAsync(userId, cancellationToken);

        InsightContext context = AnalyticsContextBuilder.Build(transactions, categories, budgets, goals, today);

        return InsightEngine.Generate(context);
    }
}
