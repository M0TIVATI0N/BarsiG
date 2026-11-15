using System.Globalization;

using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Entities;
using FinanceFlow.Domain.Enums;
using FinanceFlow.Domain.Validation;

namespace FinanceFlow.Domain.Analytics;

/// <summary>
/// Pure calculations over an immutable set of transactions.
/// Every method is a total function: the same input always produces the same output,
/// no state is mutated and nothing is read from the outside world.
/// </summary>
public static class AnalyticsCalculator
{
    /// <summary>Returns the total income of the given transactions.</summary>
    public static Money TotalIncome(IEnumerable<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        // The predicate of the kind is a curried function: the kind of the income is
        // applied now, the transaction is applied later.
        return TransactionClassifier.Total(transactions, TransactionClassifier.OfKind(TransactionKind.Income));
    }

    /// <summary>Returns the total expense of the given transactions.</summary>
    public static Money TotalExpense(IEnumerable<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        return TransactionClassifier.Total(transactions, TransactionClassifier.OfKind(TransactionKind.Expense));
    }

    /// <summary>Returns the biggest transaction of the given transactions.</summary>
    public static Money LargestTransaction(IEnumerable<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        return TransactionClassifier.Largest(transactions);
    }

    /// <summary>Returns the number of the transactions of every size.</summary>
    public static IReadOnlyDictionary<TransactionSize, int> CountBySize(IEnumerable<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        return TransactionClassifier.CountBySize(transactions);
    }

    /// <summary>Returns income minus expense.</summary>
    public static Money NetBalance(IEnumerable<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        return TotalIncome(transactions) - TotalExpense(transactions);
    }

    /// <summary>
    /// Builds a dense monthly series: every requested month is present, even the empty ones.
    /// A dense series is what makes a chart readable.
    /// </summary>
    public static IReadOnlyList<MonthlyPoint> MonthlySeries(
        IEnumerable<Transaction> transactions,
        DateOnly from,
        int months)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentOutOfRangeException.ThrowIfNegative(months);

        DateOnly last = from.AddMonths(months - 1);

        Dictionary<DateOnly, (long Income, long Expense)> buckets = transactions
            .Where(transaction => transaction.OccurredOn >= from && transaction.OccurredOn <= last)
            .GroupBy(transaction => MonthStart(transaction.OccurredOn.Year, transaction.OccurredOn.Month))
            .ToDictionary(
                group => group.Key,
                group => (
                    Income: group.Where(t => t.Kind == TransactionKind.Income).Sum(t => t.Amount.Cents),
                    Expense: group.Where(t => t.Kind == TransactionKind.Expense).Sum(t => t.Amount.Cents)));

        return Enumerable.Range(0, months)
            .Select(offset => from.AddMonths(offset))
            .Select(month =>
            {
                buckets.TryGetValue(MonthStart(month.Year, month.Month), out (long Income, long Expense) bucket);
                Money income = new(bucket.Income);
                Money expense = new(bucket.Expense);
                return new MonthlyPoint(
                    month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    income,
                    expense,
                    income - expense);
            })
            .ToList();
    }

    /// <summary>
    /// Splits the transactions of one kind into per category totals, sorted from the largest one.
    /// The share of every category is a number between zero and one.
    /// </summary>
    public static IReadOnlyList<CategoryBreakdown> BreakdownByCategory(
        IEnumerable<Transaction> transactions,
        IReadOnlyCollection<Category> categories,
        TransactionKind kind)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(categories);

        Dictionary<Guid, Category> byId = categories.ToDictionary(category => category.Id);
        Money total = kind == TransactionKind.Income
            ? TotalIncome(transactions)
            : TotalExpense(transactions);

        return transactions
            .Where(transaction => transaction.Kind == kind)
            .GroupBy(transaction => transaction.CategoryId)
            .Select(group =>
            {
                long cents = group.Sum(transaction => transaction.Amount.Cents);
                byId.TryGetValue(group.Key, out Category? category);
                return new CategoryBreakdown(
                    group.Key,
                    category?.Name ?? "Unknown",
                    category?.ColorHex ?? "#94A3B8",
                    category?.Icon ?? "help",
                    new Money(cents),
                    total.IsZero ? 0d : (double)cents / total.Cents);
            })
            .OrderByDescending(item => item.Total.Cents)
            .ToList();
    }

    /// <summary>
    /// Builds the running balance line: after every day the balance is the sum of all
    /// income minus the sum of all expenses up to that day.
    /// </summary>
    public static IReadOnlyList<TrendPoint> CumulativeBalance(IEnumerable<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        List<TrendPoint> points = [];
        long running = 0;
        int index = 0;

        foreach (IGrouping<DateOnly, Transaction> day in transactions
            .OrderBy(transaction => transaction.OccurredOn)
            .GroupBy(transaction => transaction.OccurredOn))
        {
            running += day.Sum(transaction =>
                transaction.Kind == TransactionKind.Income
                    ? transaction.Amount.Cents
                    : -transaction.Amount.Cents);

            points.Add(new TrendPoint(
                index++,
                day.Key.ToString("dd.MM", CultureInfo.InvariantCulture),
                running / 100m));
        }

        return points;
    }

    /// <summary>
    /// The share of the income that was kept instead of spent, as a number between zero and one.
    /// </summary>
    public static decimal SavingsRate(Money income, Money expense)
    {
        if (income.IsZero || income.IsNegative)
        {
            return 0m;
        }

        decimal rate = (income.ToDecimal() - expense.ToDecimal()) / income.ToDecimal();
        return decimal.Round(rate, 4, MidpointRounding.ToEven);
    }

    /// <summary>The average size of a single transaction.</summary>
    public static decimal AverageTransactionAmount(IEnumerable<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        List<Transaction> list = transactions.ToList();
        if (list.Count == 0)
        {
            return 0m;
        }

        long cents = list.Sum(transaction => transaction.Amount.Cents);
        return (decimal)cents / list.Count / 100m;
    }

    /// <summary>The average number of transactions per month over the given period.</summary>
    public static decimal AverageTransactionsPerMonth(IEnumerable<Transaction> transactions, int months)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        if (months <= 0)
        {
            return 0m;
        }

        return (decimal)transactions.Count() / months;
    }

    /// <summary>The day on which the user spent the most money.</summary>
    public static DateOnly? MostExpensiveDay(IEnumerable<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        return transactions
            .Where(transaction => transaction.Kind == TransactionKind.Expense)
            .GroupBy(transaction => transaction.OccurredOn)
            .Select(group => new { Day = group.Key, Cents = group.Sum(transaction => transaction.Amount.Cents) })
            .OrderByDescending(item => item.Cents)
            .Select(item => (DateOnly?)item.Day)
            .FirstOrDefault();
    }

    /// <summary>Classifies how much of a budget limit has been used.</summary>
    public static BudgetUsage ClassifyBudgetUsage(Money limit, Money spent)
    {
        if (limit.IsZero || limit.IsNegative)
        {
            return BudgetUsage.NotStarted;
        }

        double ratio = (double)spent.Cents / limit.Cents;

        if (ratio >= 1d)
        {
            return BudgetUsage.Exceeded;
        }

        return ratio >= 0.85d ? BudgetUsage.AlmostReached : BudgetUsage.OnTrack;
    }

    /// <summary>The daily expense series of the last <paramref name="days"/> days.</summary>
    public static IReadOnlyList<TrendPoint> DailyExpenses(IEnumerable<Transaction> transactions, int days)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentOutOfRangeException.ThrowIfNegative(days);

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly from = today.AddDays(-days + 1);

        Dictionary<DateOnly, long> byDay = transactions
            .Where(transaction => transaction.Kind == TransactionKind.Expense)
            .Where(transaction => transaction.OccurredOn >= from && transaction.OccurredOn <= today)
            .GroupBy(transaction => transaction.OccurredOn)
            .ToDictionary(group => group.Key, group => group.Sum(transaction => transaction.Amount.Cents));

        return Enumerable.Range(0, days)
            .Select(offset => from.AddDays(offset))
            .Select((day, index) => new TrendPoint(
                index,
                day.ToString("dd.MM", CultureInfo.InvariantCulture),
                (byDay.TryGetValue(day, out long cents) ? cents : 0L) / 100m))
            .ToList();
    }

    private static DateOnly MonthStart(int year, int month) => new(year, month, 1);
}
