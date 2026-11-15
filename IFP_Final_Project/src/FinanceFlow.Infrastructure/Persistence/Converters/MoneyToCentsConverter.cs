using FinanceFlow.Domain.Common;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FinanceFlow.Infrastructure.Persistence.Converters;

/// <summary>
/// Stores a <see cref="Money"/> value as a whole number of the smallest currency unit.
/// SQLite has no decimal type, and an integer amount also removes any rounding error.
/// </summary>
public sealed class MoneyToCentsConverter : ValueConverter<Money, long>
{
    /// <summary>Creates the converter.</summary>
    public MoneyToCentsConverter()
        : base(money => money.Cents, cents => new Money(cents))
    {
    }
}
