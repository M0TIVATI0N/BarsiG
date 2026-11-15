using FinanceFlow.Web.Components.Shared;

namespace FinanceFlow.ComponentTests;

public sealed class ChartGeometryTests
{
    [Fact]
    public void Range_FindsTheSmallestAndTheLargestValue()
    {
        (double min, double max) = ChartGeometry.Range(new[] { 5d, 1d, 9d });

        Assert.Equal(1d, min);
        Assert.Equal(9d, max);
    }

    [Fact]
    public void Range_OfAnEmptySeriesIsSafe()
    {
        (double min, double max) = ChartGeometry.Range(Array.Empty<double>());

        Assert.Equal(0d, min);
        Assert.Equal(1d, max);
    }

    [Fact]
    public void Range_KeepsZeroInsideTheRangeWhenItIsAsked()
    {
        (double min, double max) = ChartGeometry.Range(new[] { 5d, -3d }, includeZero: true);

        Assert.Equal(-3d, min);
        Assert.Equal(5d, max);
    }

    [Fact]
    public void Range_OfEqualValuesGetsANonZeroWidth()
    {
        (double min, double max) = ChartGeometry.Range(new[] { 4d, 4d });

        Assert.True(max > min);
    }

    [Fact]
    public void Project_PutsTheFirstValueAtTheBottom()
    {
        IReadOnlyList<(double X, double Y)> points = ChartGeometry.Project(
            new[] { 0d, 10d },
            100d,
            50d,
            0d,
            0d,
            0d,
            0d,
            0d,
            10d);

        Assert.Equal(2, points.Count);
        Assert.Equal(50d, points[0].Y, 3);
        Assert.Equal(0d, points[1].Y, 3);
    }

    [Fact]
    public void BuildLinePath_StartsWithAMoveCommand()
    {
        string path = ChartGeometry.BuildLinePath([(0d, 10d), (10d, 20d)]);

        Assert.StartsWith("M 0 10", path);
        Assert.Contains("L 10 20", path);
    }

    [Fact]
    public void BuildAreaPath_ClosesTheShape()
    {
        string path = ChartGeometry.BuildAreaPath([(0d, 10d), (10d, 20d)], 40d);

        Assert.EndsWith("Z", path);
    }

    [Fact]
    public void BuildArcPath_DescribesACurve()
    {
        string path = ChartGeometry.BuildArcPath(50d, 50d, 40d, 0d, 90d);

        Assert.StartsWith("M ", path);
        Assert.Contains("A 40 40", path);
    }

    [Fact]
    public void BuildArcPath_HandlesAFullCircle()
    {
        string path = ChartGeometry.BuildArcPath(50d, 50d, 40d, 0d, 360d);

        Assert.Contains("A 40 40", path);
    }
}
