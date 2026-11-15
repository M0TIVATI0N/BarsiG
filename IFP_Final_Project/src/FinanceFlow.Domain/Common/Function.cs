namespace FinanceFlow.Domain.Common;

/// <summary>
/// Helpers for function composition.
/// A composed function is still a plain function, which makes it easy to unit test
/// and to pass around as a delegate.
/// </summary>
public static class Function
{
    /// <summary>Returns the identity function.</summary>
    public static Func<T, T> Identity<T>() => static value => value;

    /// <summary>
    /// Composes two functions from left to right: <c>Compose(f, g)(x) == g(f(x))</c>.
    /// </summary>
    public static Func<T, TResult> Compose<T, TIntermediate, TResult>(
        Func<T, TIntermediate> first,
        Func<TIntermediate, TResult> second) =>
        value => second(first(value));

    /// <summary>
    /// Composes three functions from left to right.
    /// </summary>
    public static Func<T, TResult> Compose<T, TFirst, TSecond, TResult>(
        Func<T, TFirst> first,
        Func<TFirst, TSecond> second,
        Func<TSecond, TResult> third) =>
        value => third(second(first(value)));

    /// <summary>
    /// Builds a function from a value and a function that receives it.
    /// This is the classic <c>flip</c> of a curried function and is used to turn
    /// a two argument operation into a one argument delegate.
    /// </summary>
    public static Func<TSecond, TResult> Flip<TFirst, TSecond, TResult>(
        Func<TFirst, TSecond, TResult> function,
        TFirst first) =>
        Curry(function, first);

    /// <summary>
    /// Curries a two argument function: the first argument is applied now, the second one
    /// is applied later. The result is a function of one argument, therefore the operation
    /// can be stored, passed around and combined with other functions.
    /// </summary>
    public static Func<TSecond, TResult> Curry<TFirst, TSecond, TResult>(
        Func<TFirst, TSecond, TResult> function,
        TFirst first) =>
        second => function(first, second);

    /// <summary>
    /// Curries a three argument function. The first argument is applied now, the two
    /// remaining ones are applied later.
    /// </summary>
    public static Func<TSecond, TThird, TResult> Curry<TFirst, TSecond, TThird, TResult>(
        Func<TFirst, TSecond, TThird, TResult> function,
        TFirst first) =>
        (second, third) => function(first, second, third);

    /// <summary>
    /// Threads a value through one function. It is the shortest member of the family,
    /// it reads exactly like the mathematical notation <c>f(x)</c>.
    /// </summary>
    public static TResult Pipe<T, TResult>(T value, Func<T, TResult> first) =>
        first(value);

    /// <summary>
    /// Threads a value through two functions. The chain reads from left to right:
    /// <c>Pipe(value, first, second)</c> is <c>second(first(value))</c>.
    /// </summary>
    public static TResult Pipe<T, TIntermediate, TResult>(
        T value,
        Func<T, TIntermediate> first,
        Func<TIntermediate, TResult> second) =>
        second(first(value));

    /// <summary>
    /// Threads a value through three functions. The chain reads from left to right:
    /// <c>Pipe(value, first, second, third)</c> is <c>third(second(first(value)))</c>.
    /// </summary>
    public static TResult Pipe<T, TFirst, TSecond, TResult>(
        T value,
        Func<T, TFirst> first,
        Func<TFirst, TSecond> second,
        Func<TSecond, TResult> third) =>
        third(second(first(value)));
}
