using System.Globalization;

using FinanceFlow.Domain.Common;

namespace FinanceFlow.Domain.Analytics;

/// <summary>
/// A pure forecasting engine.
/// It fits a least squares trend line through the monthly expenses of the past and
/// extrapolates it for the next months. The confidence value is the coefficient of
/// determination of the fit, so a noisy history honestly produces a low confidence.
/// </summary>
public static class ForecastingEngine
{
    /// <summary>The default number of predicted months.</summary>
    public const int DefaultHorizon = 3;

    /// <summary>
    /// Predicts the expenses of the next months using a linear trend.
    /// </summary>
    public static ForecastResult Forecast(IReadOnlyList<MonthlyPoint> history, int horizon = DefaultHorizon)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentOutOfRangeException.ThrowIfNegative(horizon);

        List<decimal> expenses = history.Select(point => point.Expense.ToDecimal()).ToList();

        if (expenses.Count == 0)
        {
            return Empty("insufficient-data");
        }

        if (expenses.Count == 1)
        {
            return ConstantForecast(history, expenses[0], horizon, "single-point");
        }

        (double slope, double intercept, double confidence) = LeastSquares(expenses);
        List<double> residuals = expenses
            .Select((value, index) => (double)value - ((slope * index) + intercept))
            .ToList();
        double deviation = StandardDeviation(residuals);

        DateOnly lastMonth = DateOnly.ParseExact(
            history[^1].Month,
            "yyyy-MM",
            CultureInfo.InvariantCulture);

        List<ForecastPoint> points = [];
        for (int step = 1; step <= horizon; step++)
        {
            double predicted = (slope * (expenses.Count - 1 + step)) + intercept;
            double spread = deviation * 1.28d;

            points.Add(new ForecastPoint(
                lastMonth.AddMonths(step).ToString("yyyy-MM", CultureInfo.InvariantCulture),
                Math.Round((decimal)predicted, 2, MidpointRounding.ToEven),
                Math.Round((decimal)Math.Max(0d, predicted - spread), 2, MidpointRounding.ToEven),
                Math.Round((decimal)Math.Max(0d, predicted + spread), 2, MidpointRounding.ToEven)));
        }

        decimal average = expenses.Average();

        return new ForecastResult(
            points,
            Math.Round((decimal)slope, 2, MidpointRounding.ToEven),
            Math.Round(confidence, 4, MidpointRounding.ToEven),
            "linear-trend",
            points.Count > 0 ? points[0].Predicted : 0m,
            Math.Round(average, 2, MidpointRounding.ToEven));
    }

    /// <summary>
    /// Predicts the expenses of the next months using a moving average of the last three months.
    /// It is a more stable, but less flexible, alternative to the linear trend.
    /// </summary>
    public static ForecastResult ForecastMovingAverage(IReadOnlyList<MonthlyPoint> history, int horizon = DefaultHorizon)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentOutOfRangeException.ThrowIfNegative(horizon);

        List<decimal> expenses = history.Select(point => point.Expense.ToDecimal()).ToList();

        if (expenses.Count == 0)
        {
            return Empty("insufficient-data");
        }

        if (expenses.Count == 1)
        {
            return ConstantForecast(history, expenses[0], horizon, "single-point");
        }

        decimal average = expenses.Count >= 3
            ? expenses.Skip(expenses.Count - 3).Average()
            : expenses.Average();

        DateOnly lastMonth = DateOnly.ParseExact(
            history[^1].Month,
            "yyyy-MM",
            CultureInfo.InvariantCulture);

        List<ForecastPoint> points = [];
        for (int step = 1; step <= horizon; step++)
        {
            points.Add(new ForecastPoint(
                lastMonth.AddMonths(step).ToString("yyyy-MM", CultureInfo.InvariantCulture),
                Math.Round(average, 2, MidpointRounding.ToEven),
                Math.Round(average * 0.85m, 2, MidpointRounding.ToEven),
                Math.Round(average * 1.15m, 2, MidpointRounding.ToEven)));
        }

        return new ForecastResult(
            points,
            0m,
            0.5d,
            "moving-average",
            points[0].Predicted,
            Math.Round(expenses.Average(), 2, MidpointRounding.ToEven));
    }

    private static ForecastResult ConstantForecast(
        IReadOnlyList<MonthlyPoint> history,
        decimal value,
        int horizon,
        string method)
    {
        DateOnly lastMonth = DateOnly.ParseExact(
            history[^1].Month,
            "yyyy-MM",
            CultureInfo.InvariantCulture);

        List<ForecastPoint> points = [];
        for (int step = 1; step <= horizon; step++)
        {
            points.Add(new ForecastPoint(
                lastMonth.AddMonths(step).ToString("yyyy-MM", CultureInfo.InvariantCulture),
                Math.Round(value, 2, MidpointRounding.ToEven),
                Math.Round(value * 0.9m, 2, MidpointRounding.ToEven),
                Math.Round(value * 1.1m, 2, MidpointRounding.ToEven)));
        }

        return new ForecastResult(points, 0m, 0.25d, method, points[0].Predicted, Math.Round(value, 2, MidpointRounding.ToEven));
    }

    private static ForecastResult Empty(string method) =>
        new([], 0m, 0d, method, 0m, 0m);

    private static (double Slope, double Intercept, double Confidence) LeastSquares(IReadOnlyList<decimal> values)
    {
        int count = values.Count;
        double sumX = 0d;
        double sumY = 0d;
        double sumXY = 0d;
        double sumXX = 0d;

        for (int index = 0; index < count; index++)
        {
            double x = index;
            double y = (double)values[index];
            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumXX += x * x;
        }

        double denominator = (count * sumXX) - (sumX * sumX);
        double slope = Math.Abs(denominator) < 1e-9 ? 0d : ((count * sumXY) - (sumX * sumY)) / denominator;
        double intercept = (sumY - (slope * sumX)) / count;

        double mean = sumY / count;
        double totalVariation = values.Sum(value => Math.Pow((double)value - mean, 2d));
        double residualVariation = values
            .Select((value, index) => Math.Pow((double)value - ((slope * index) + intercept), 2d))
            .Sum();

        double confidence = totalVariation < 1e-9 ? 1d : 1d - (residualVariation / totalVariation);
        confidence = Math.Clamp(confidence, 0d, 1d);

        return (slope, intercept, confidence);
    }

    private static double StandardDeviation(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0d;
        }

        double mean = values.Average();
        double variance = values.Sum(value => Math.Pow(value - mean, 2d)) / values.Count;
        return Math.Sqrt(variance);
    }
}
