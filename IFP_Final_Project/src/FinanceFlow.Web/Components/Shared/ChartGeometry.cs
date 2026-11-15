using System.Globalization;

namespace FinanceFlow.Web.Components.Shared;

/// <summary>
/// Pure helper functions for the SVG charts.
/// The geometry is calculated in C#, so the charts need no JavaScript at all.
/// Every number is written with a point as the decimal separator, because the
/// markup of SVG only understands the point.
/// </summary>
public static class ChartGeometry
{
    /// <summary>Returns the smallest and the largest value of a series.</summary>
    public static (double Min, double Max) Range(IEnumerable<double> values, bool includeZero = false)
    {
        List<double> list = values.ToList();

        if (list.Count == 0)
        {
            return (0d, 1d);
        }

        double min = list.Min();
        double max = list.Max();

        if (includeZero)
        {
            min = Math.Min(min, 0d);
            max = Math.Max(max, 0d);
        }

        if (Math.Abs(max - min) < 1e-9)
        {
            max = min + 1d;
        }

        return (min, max);
    }

    /// <summary>
    /// Projects the values onto the coordinate system of the chart.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> Project(
        IReadOnlyList<double> values,
        double width,
        double height,
        double paddingLeft,
        double paddingRight,
        double paddingTop,
        double paddingBottom,
        double min,
        double max)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
        {
            return [];
        }

        double usableWidth = Math.Max(width - paddingLeft - paddingRight, 1d);
        double usableHeight = Math.Max(height - paddingTop - paddingBottom, 1d);
        double range = Math.Max(max - min, 1e-9);
        double step = values.Count == 1 ? 0d : usableWidth / (values.Count - 1);

        return values
            .Select((value, index) => (
                X: paddingLeft + (step * index),
                Y: paddingTop + usableHeight - (((value - min) / range) * usableHeight)))
            .ToList();
    }

    /// <summary>Builds the path of a polyline.</summary>
    public static string BuildLinePath(IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(' ', points.Select((point, index) =>
            index == 0
                ? $"M {Number(point.X)} {Number(point.Y)}"
                : $"L {Number(point.X)} {Number(point.Y)}"));
    }

    /// <summary>Builds the path of an area between a line and the baseline.</summary>
    public static string BuildAreaPath(IReadOnlyList<(double X, double Y)> points, double baseline)
    {
        if (points.Count == 0)
        {
            return string.Empty;
        }

        string line = BuildLinePath(points);

        return $"{line} L {Number(points[^1].X)} {Number(baseline)} L {Number(points[0].X)} {Number(baseline)} Z";
    }

    /// <summary>
    /// Builds the path of a ring segment. The angles are given in degrees,
    /// zero degrees points to the top and the values grow clockwise.
    /// </summary>
    public static string BuildArcPath(double centerX, double centerY, double radius, double startAngle, double endAngle)
    {
        const double fullCircle = 360d;

        if (endAngle - startAngle >= fullCircle - 0.01d)
        {
            endAngle = startAngle + 359.99d;
        }

        double start = ToRadians(startAngle - 90d);
        double end = ToRadians(endAngle - 90d);

        double startX = centerX + (radius * Math.Cos(start));
        double startY = centerY + (radius * Math.Sin(start));
        double endX = centerX + (radius * Math.Cos(end));
        double endY = centerY + (radius * Math.Sin(end));

        double largeArc = endAngle - startAngle > 180d ? 1d : 0d;

        return $"M {Number(startX)} {Number(startY)} A {Number(radius)} {Number(radius)} 0 {Number(largeArc)} 1 {Number(endX)} {Number(endY)}";
    }

    /// <summary>
    /// Writes a number in the format that the SVG markup understands.
    /// </summary>
    public static string Number(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
