using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Enums;

namespace FinanceFlow.Domain.Analytics;

/// <summary>How closely a budget limit has been reached.</summary>
public enum BudgetUsage
{
    /// <summary>Nothing has been spent yet.</summary>
    NotStarted = 0,

    /// <summary>The budget is used normally.</summary>
    OnTrack = 1,

    /// <summary>More than 85 percent of the limit is used.</summary>
    AlmostReached = 2,

    /// <summary>The limit is exceeded.</summary>
    Exceeded = 3
}

/// <summary>Income, expense and net amount of one calendar month.</summary>
public sealed record MonthlyPoint(string Month, Money Income, Money Expense, Money Net);

/// <summary>The share of one category inside a period.</summary>
public sealed record CategoryBreakdown(
    Guid CategoryId,
    string Name,
    string ColorHex,
    string Icon,
    Money Total,
    double Share);

/// <summary>A point of a line or bar chart.</summary>
public sealed record TrendPoint(int Index, string Label, decimal Value);

/// <summary>The state of one monthly budget.</summary>
public sealed record BudgetStatus(
    Guid CategoryId,
    string CategoryName,
    string ColorHex,
    Money Limit,
    Money Spent,
    double Ratio,
    BudgetUsage Usage);

/// <summary>One predicted month of the forecast.</summary>
public sealed record ForecastPoint(string Month, decimal Predicted, decimal Lower, decimal Upper);

/// <summary>The full result of the forecasting engine.</summary>
public sealed record ForecastResult(
    IReadOnlyList<ForecastPoint> Points,
    decimal TrendPerMonth,
    double Confidence,
    string Method,
    decimal PredictedNextMonth,
    decimal MonthlyAverage);

/// <summary>A single piece of smart advice.</summary>
public sealed record Insight(
    InsightKind Kind,
    InsightSeverity Severity,
    string Title,
    string Description,
    string? Action = null);

/// <summary>Everything the insight engine needs to produce its advice.</summary>
public sealed record InsightContext(
    IReadOnlyList<MonthlyPoint> History,
    IReadOnlyList<BudgetStatus> Budgets,
    IReadOnlyList<CategoryBreakdown> ExpenseBreakdown,
    IReadOnlyList<TrendPoint> DailyExpenses,
    Money CurrentBalance,
    IReadOnlyList<GoalProgress> Goals,
    int DaysWithoutActivity);

/// <summary>The progress of one savings goal.</summary>
public sealed record GoalProgress(string Title, Money Target, Money Saved, double Ratio);
