using FinanceFlow.Domain.Analytics;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Enums;
using FinanceFlow.Domain.Validation;

namespace FinanceFlow.Application.Dtos;

/// <summary>The signed in user, as it is known by the interface layer.</summary>
/// <param name="UserId">The identifier of the user.</param>
/// <param name="Email">The e-mail address of the user, or an empty string for phone-only accounts.</param>
/// <param name="DisplayName">The display name of the user.</param>
/// <param name="Role">The role of the user inside the Demo Bank.</param>
public sealed record UserSession(Guid UserId, string Email, string DisplayName, string Role = "User");

/// <summary>The data of the sign in form.</summary>
public sealed record LoginRequest(string Email, string Password, string? PhoneNumber = null);

/// <summary>The data of the registration form.</summary>
public sealed record RegisterRequest(
    string Email,
    string DisplayName,
    string Password,
    string ConfirmPassword,
    string? PhoneNumber = null);

/// <summary>One transaction prepared for the interface layer.</summary>
public sealed record TransactionItem(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string ColorHex,
    string Icon,
    Money Amount,
    TransactionKind Kind,
    DateOnly OccurredOn,
    string? Note,
    bool IsRecurring);

/// <summary>The filter of the transaction list.</summary>
public sealed record TransactionFilter(
    TransactionKind? Kind = null,
    Guid? CategoryId = null,
    string? Search = null,
    DateOnly? From = null,
    DateOnly? To = null,
    int Page = 1,
    int PageSize = 10);

/// <summary>One page of a filtered and sorted list.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

/// <summary>One category prepared for the interface layer.</summary>
public sealed record CategoryItem(
    Guid Id,
    string Name,
    TransactionKind Kind,
    string ColorHex,
    string Icon,
    bool IsArchived,
    Money SpentThisMonth,
    int TransactionCount);

/// <summary>One budget prepared for the interface layer.</summary>
public sealed record BudgetItem(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string ColorHex,
    Money Limit,
    Money Spent,
    double Ratio,
    BudgetUsage Usage,
    int Year,
    int Month);

/// <summary>One savings goal prepared for the interface layer.</summary>
public sealed record GoalItem(
    Guid Id,
    string Title,
    Money Target,
    Money Saved,
    double Ratio,
    DateOnly? TargetDate,
    GoalStatus Status,
    string? Note,
    int? DaysLeft);

/// <summary>The content of the dashboard page.</summary>
public sealed record DashboardSummary(
    Money Balance,
    Money Income,
    Money Expense,
    decimal SavingsRate,
    int TransactionCount,
    Money SpentThisMonth,
    IReadOnlyList<MonthlyPoint> MonthlyHistory,
    IReadOnlyList<CategoryBreakdown> TopExpenseCategories,
    IReadOnlyList<BudgetStatus> Budgets,
    IReadOnlyList<Insight> Insights,
    IReadOnlyList<TransactionItem> RecentTransactions,
    IReadOnlyList<GoalItem> Goals);

/// <summary>The content of the analytics page.</summary>
public sealed record AnalyticsReport(
    IReadOnlyList<MonthlyPoint> Monthly,
    IReadOnlyList<CategoryBreakdown> ExpenseBreakdown,
    IReadOnlyList<CategoryBreakdown> IncomeBreakdown,
    IReadOnlyList<TrendPoint> BalanceLine,
    IReadOnlyList<TrendPoint> DailyExpenses,
    decimal SavingsRate,
    decimal AverageTransaction,
    decimal AveragePerMonth,
    DateOnly? MostExpensiveDay,
    Money TotalIncome,
    Money TotalExpense);

/// <summary>Both forecasting methods side by side, together with the history they are based on.</summary>
public sealed record ForecastReport(
    IReadOnlyList<MonthlyPoint> History,
    ForecastResult LinearTrend,
    ForecastResult MovingAverage);
