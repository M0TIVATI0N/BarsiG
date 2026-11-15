using System.Collections.Concurrent;
using FinanceFlow.Application.Abstractions;

namespace FinanceFlow.Infrastructure.Banking;

/// <summary>
/// Blocks an account for a while after too many failed attempts to sign in.
/// The counter lives in the memory of the server, it is enough for the demonstration
/// and it does not slow down the database with a write for every wrong password.
/// </summary>
public sealed class LoginThrottle : ILoginThrottle
{
    /// <summary>The number of failed attempts that block the account.</summary>
    public const int MaxAttempts = 5;

    /// <summary>The time the account stays blocked.</summary>
    public static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(5);

    /// <summary>The time after which the failures are counted again.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, Attempt> _attempts = new();
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the throttle.</summary>
    public LoginThrottle()
        : this(TimeProvider.System)
    {
    }

    /// <summary>Creates the throttle with an own time provider, the tests use it.</summary>
    public LoginThrottle(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public bool IsBlocked(string key, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;

        if (!_attempts.TryGetValue(key, out Attempt? attempt))
        {
            return false;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        if (now - attempt.FirstFailure > Window)
        {
            _attempts.TryRemove(key, out _);
            return false;
        }

        if (attempt.Count < MaxAttempts)
        {
            return false;
        }

        DateTimeOffset unblockAt = attempt.FirstFailure + BlockDuration;
        remaining = unblockAt - now;

        return remaining > TimeSpan.Zero;
    }

    /// <inheritdoc />
    public void RegisterFailure(string key)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        _attempts.AddOrUpdate(
            key,
            _ => new Attempt(1, now),
            (_, attempt) => now - attempt.FirstFailure > Window
                ? new Attempt(1, now)
                : attempt with { Count = attempt.Count + 1 });
    }

    /// <inheritdoc />
    public void RegisterSuccess(string key) => _attempts.TryRemove(key, out _);

    private sealed record Attempt(int Count, DateTimeOffset FirstFailure);
}
