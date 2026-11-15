namespace FinanceFlow.Application.Banking;

/// <summary>
/// A bounded, per-customer sliding-window limit for phone recipient lookups.
/// The state is process-local and contains only user identifiers and request times.
/// </summary>
public sealed class PhoneRecipientLookupThrottle
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private const int MaximumLookupsPerWindow = 8;
    private const int MaximumTrackedUsers = 10_000;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, Queue<DateTimeOffset>> _requests = [];

    /// <summary>Returns whether this customer may perform another lookup now.</summary>
    public bool TryAcquire(Guid userId, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            if (_requests.Count >= MaximumTrackedUsers && !_requests.ContainsKey(userId))
            {
                DateTimeOffset cutoff = nowUtc - Window;

                foreach (Guid staleUserId in _requests
                             .Where(pair => pair.Value.Count == 0 || pair.Value.Peek() <= cutoff)
                             .Select(pair => pair.Key)
                             .ToArray())
                {
                    _requests.Remove(staleUserId);
                }

                if (_requests.Count >= MaximumTrackedUsers)
                {
                    return false;
                }
            }

            if (!_requests.TryGetValue(userId, out Queue<DateTimeOffset>? timestamps))
            {
                timestamps = new Queue<DateTimeOffset>();
                _requests.Add(userId, timestamps);
            }

            DateTimeOffset windowStart = nowUtc - Window;

            while (timestamps.TryPeek(out DateTimeOffset oldest) && oldest <= windowStart)
            {
                timestamps.Dequeue();
            }

            if (timestamps.Count >= MaximumLookupsPerWindow)
            {
                return false;
            }

            timestamps.Enqueue(nowUtc);
            return true;
        }
    }
}
