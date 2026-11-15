namespace FinanceFlow.Domain.Common;

/// <summary>
/// Hand written lazy sequence operators.
/// They mirror the standard LINQ operators, but they are written from scratch with
/// <c>yield return</c>, which makes the deferred execution explicit and is the direct
/// continuation of the course assignment "Extension Methods and LINQ".
/// </summary>
public static class LazyOperators
{
    /// <summary>Lazily keeps only the elements that satisfy the predicate.</summary>
    public static IEnumerable<T> Filter<T>(this IEnumerable<T> source, Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);

        foreach (T item in source)
        {
            if (predicate(item))
            {
                yield return item;
            }
        }
    }

    /// <summary>Lazily projects every element into a new value.</summary>
    public static IEnumerable<R> Transform<T, R>(this IEnumerable<T> source, Func<T, R> transform)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(transform);

        foreach (T item in source)
        {
            yield return transform(item);
        }
    }

    /// <summary>
    /// Folds a sequence into a single value using an explicit accumulator.
    /// The accumulator is never mutated in place: a new value is produced on every step.
    /// </summary>
    public static R Fold<T, R>(this IEnumerable<T> source, R seed, Func<R, T, R> step)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(step);

        R accumulator = seed;
        foreach (T item in source)
        {
            accumulator = step(accumulator, item);
        }

        return accumulator;
    }

    /// <summary>Lazily produces the first <paramref name="count"/> elements.</summary>
    public static IEnumerable<T> Head<T>(this IEnumerable<T> source, int count)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        int taken = 0;
        foreach (T item in source)
        {
            if (taken >= count)
            {
                yield break;
            }

            taken++;
            yield return item;
        }
    }

    /// <summary>
    /// Returns the first element that satisfies the predicate, or the fallback value.
    /// It is a safe alternative to <c>First</c> for a possibly empty sequence.
    /// </summary>
    public static T? FirstOrFallback<T>(this IEnumerable<T> source, Func<T, bool> predicate, T fallback)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);

        foreach (T item in source)
        {
            if (predicate(item))
            {
                return item;
            }
        }

        return fallback;
    }
}
