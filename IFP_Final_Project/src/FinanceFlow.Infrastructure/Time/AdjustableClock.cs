using FinanceFlow.Application.Abstractions;

namespace FinanceFlow.Infrastructure.Time;

/// <summary>
/// The clock of the server.
/// It can be moved forward, which is used by the demonstration of the interest:
/// the simulated months let the students show how a deposit grows without waiting a year.
/// The offset is only changed by the development tools, never by the ordinary use cases.
/// </summary>
public sealed class AdjustableClock : IClock
{
    private long _offsetTicks;

    /// <summary>The current moment in UTC, shifted by the simulated offset.</summary>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref _offsetTicks));

    /// <summary>Today according to the clock of the server.</summary>
    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);

    /// <summary>Moves the clock forward or backward.</summary>
    /// <param name="amount">The size of the shift. It may be negative.</param>
    public void Advance(TimeSpan amount) => Interlocked.Add(ref _offsetTicks, amount.Ticks);

    /// <summary>The size of the simulated shift.</summary>
    public TimeSpan Offset => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    /// <summary>Removes the simulated shift.</summary>
    public void Reset() => Interlocked.Exchange(ref _offsetTicks, 0);
}
