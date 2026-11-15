using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FinanceFlow.Infrastructure.Persistence.Converters;

/// <summary>
/// Stores a moment in time as the number of milliseconds since the Unix epoch.
/// SQLite can compare and order a number, but it cannot order a stored date,
/// therefore the moment is kept as a plain integer.
/// </summary>
public sealed class DateTimeOffsetToUnixMillisecondsConverter : ValueConverter<DateTimeOffset, long>
{
    /// <summary>Creates the converter.</summary>
    public DateTimeOffsetToUnixMillisecondsConverter()
        : base(
            value => value.ToUnixTimeMilliseconds(),
            value => DateTimeOffset.FromUnixTimeMilliseconds(value))
    {
    }
}
