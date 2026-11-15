using System.Text;

namespace FinanceFlow.Application.Export;

/// <summary>
/// A small pure writer for the comma separated values format.
/// It follows the RFC 4180 rules: a value is quoted when it contains a separator,
/// a quote or a line break, and a quote inside a value is doubled.
/// </summary>
public static class CsvWriter
{
    /// <summary>Builds a CSV document from a header row and the data rows.</summary>
    public static string Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        StringBuilder builder = new();

        builder.AppendLine(string.Join(',', headers.Select(Escape)));

        foreach (IReadOnlyList<string> row in rows)
        {
            builder.AppendLine(string.Join(',', row.Select(Escape)));
        }

        return builder.ToString();
    }

    /// <summary>Escapes a single value.</summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        bool needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');

        if (!needsQuotes)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
