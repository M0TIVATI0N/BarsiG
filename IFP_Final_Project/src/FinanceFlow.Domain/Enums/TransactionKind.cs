namespace FinanceFlow.Domain.Enums;

/// <summary>Direction of a financial transaction.</summary>
public enum TransactionKind
{
    /// <summary>Money that arrives: salary, refunds, gifts.</summary>
    Income = 0,

    /// <summary>Money that leaves: rent, groceries, subscriptions.</summary>
    Expense = 1
}

/// <summary>Lifecycle state of a savings goal.</summary>
public enum GoalStatus
{
    /// <summary>The goal is being filled.</summary>
    Active = 0,

    /// <summary>The target amount has been reached.</summary>
    Completed = 1,

    /// <summary>The goal was stopped before completion.</summary>
    Cancelled = 2
}

/// <summary>How urgent an insight is.</summary>
public enum InsightSeverity
{
    /// <summary>Neutral information.</summary>
    Info = 0,

    /// <summary>Something worth noticing.</summary>
    Warning = 1,

    /// <summary>Something that needs attention now.</summary>
    Critical = 2
}

/// <summary>Category of an insight.</summary>
public enum InsightKind
{
    /// <summary>Comparison with the previous period.</summary>
    Trend = 0,

    /// <summary>Budget related observation.</summary>
    Budget = 1,

    /// <summary>An unusual single transaction.</summary>
    Anomaly = 2,

    /// <summary>Savings goal related observation.</summary>
    Goal = 3,

    /// <summary>Observation about the distribution of expenses.</summary>
    Distribution = 4,

    /// <summary>Encouraging observation.</summary>
    Positive = 5
}
