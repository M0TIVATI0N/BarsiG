using FinanceFlow.Domain.Analytics;
using FinanceFlow.Domain.Common;

namespace FinanceFlow.UnitTests;

public sealed class ForecastingEngineTests
{
    private static List<MonthlyPoint> CreateHistory(params decimal[] expenses)
    {
        DateOnly start = new(2026, 1, 1);

        return expenses
            .Select((value, index) => new MonthlyPoint(
                start.AddMonths(index).ToString("yyyy-MM"),
                Money.FromDecimal(value * 2m),
                Money.FromDecimal(value),
                Money.FromDecimal(value)))
            .ToList();
    }

    [Fact]
    public void Forecast_ReturnsTheRequestedNumberOfMonths()
    {
        ForecastResult result = ForecastingEngine.Forecast(CreateHistory(100m, 120m, 140m), 3);

        Assert.Equal(3, result.Points.Count);
        Assert.Equal("linear-trend", result.Method);
    }

    [Fact]
    public void Forecast_DetectsAGrowingTrend()
    {
        ForecastResult result = ForecastingEngine.Forecast(CreateHistory(100m, 120m, 140m, 160m), 2);

        Assert.True(result.TrendPerMonth > 0m);
        Assert.True(result.Points[0].Predicted > 160m);
    }

    [Fact]
    public void Forecast_DetectsAShrinkingTrend()
    {
        ForecastResult result = ForecastingEngine.Forecast(CreateHistory(200m, 160m, 120m, 80m), 2);

        Assert.True(result.TrendPerMonth < 0m);
        Assert.True(result.Points[0].Predicted < 80m);
    }

    [Fact]
    public void Forecast_WithoutHistory_ReturnsAnEmptyResult()
    {
        ForecastResult result = ForecastingEngine.Forecast([], 3);

        Assert.Empty(result.Points);
        Assert.Equal("insufficient-data", result.Method);
    }

    [Fact]
    public void Forecast_WithOneMonth_ReturnsAConstantPrediction()
    {
        ForecastResult result = ForecastingEngine.Forecast(CreateHistory(250m), 2);

        Assert.Equal(2, result.Points.Count);
        Assert.Equal(250m, result.Points[0].Predicted);
        Assert.Equal("single-point", result.Method);
    }

    [Fact]
    public void Forecast_KeepsTheLowerBoundBelowTheUpperBound()
    {
        ForecastResult result = ForecastingEngine.Forecast(CreateHistory(100m, 130m, 90m, 150m, 110m), 3);

        foreach (ForecastPoint point in result.Points)
        {
            Assert.True(point.Lower <= point.Predicted);
            Assert.True(point.Predicted <= point.Upper);
        }
    }

    [Fact]
    public void TheConfidenceIsBetweenZeroAndOne()
    {
        ForecastResult result = ForecastingEngine.Forecast(CreateHistory(100m, 130m, 90m, 150m, 110m), 2);

        Assert.InRange(result.Confidence, 0d, 1d);
    }

    [Fact]
    public void TheMovingAverageIsMoreStableThanTheTrend()
    {
        List<MonthlyPoint> history = CreateHistory(100m, 100m, 100m, 300m);

        ForecastResult moving = ForecastingEngine.ForecastMovingAverage(history, 1);
        ForecastResult trend = ForecastingEngine.Forecast(history, 1);

        Assert.Equal("moving-average", moving.Method);
        Assert.True(moving.Points[0].Predicted < trend.Points[0].Predicted);
    }

    [Fact]
    public void TheMovingAverageWithoutHistoryReturnsAnEmptyResult()
    {
        ForecastResult result = ForecastingEngine.ForecastMovingAverage([], 2);

        Assert.Empty(result.Points);
    }
}
