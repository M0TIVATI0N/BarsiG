namespace FinanceFlow.Domain.Common;

/// <summary>
/// Monadic helpers for <see cref="Result{T}"/>: <c>Map</c>, <c>Bind</c> and friends.
/// They allow a chain of fallible steps to be written as a single pipeline
/// without a single <c>try</c>/<c>catch</c> block.
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Projects a successful value into another value. A failure is passed through unchanged.
    /// </summary>
    public static Result<U> Map<T, U>(this Result<T> result, Func<T, U> transform) =>
        result switch
        {
            Ok<T> ok => new Ok<U>(transform(ok.Value)),
            Fail<T> fail => CreateFail<U>(fail.Error, fail.Code, fail.Details),
            _ => CreateFail<U>("Unexpected result state.", "unexpected_state", [])
        };

    /// <summary>
    /// Chains a fallible step onto a successful value. A failure short circuits the whole chain.
    /// </summary>
    public static Result<U> Bind<T, U>(this Result<T> result, Func<T, Result<U>> next) =>
        result switch
        {
            Ok<T> ok => next(ok.Value),
            Fail<T> fail => CreateFail<U>(fail.Error, fail.Code, fail.Details),
            _ => CreateFail<U>("Unexpected result state.", "unexpected_state", [])
        };

    /// <summary>
    /// Runs a side effect on a successful value and returns the original result untouched.
    /// Used only for logging and for notification fan-out, never for business logic.
    /// </summary>
    public static Result<T> Tap<T>(this Result<T> result, Action<T> action)
    {
        if (result is Ok<T> ok)
        {
            action(ok.Value);
        }

        return result;
    }

    /// <summary>Converts a result into a value, using a fallback when the result failed.</summary>
    public static T ValueOr<T>(this Result<T> result, T fallback) =>
        result is Ok<T> ok ? ok.Value : fallback;

    /// <summary>Returns <c>true</c> when the result represents success.</summary>
    public static bool IsSuccess<T>(this Result<T> result) => result is Ok<T>;

    /// <summary>Returns <c>true</c> when the unit result represents success.</summary>
    public static bool IsSuccess(this Result result) => result is Result.Success;

    /// <summary>Returns <c>true</c> when the result represents failure.</summary>
    public static bool IsFailure<T>(this Result<T> result) => result is Fail<T>;

    /// <summary>Returns <c>true</c> when the unit result represents failure.</summary>
    public static bool IsFailure(this Result result) => result is Result.Failure;

    /// <summary>
    /// Collapses a result into a single value by choosing one of two continuations.
    /// This is the only place where the two cases are inspected explicitly.
    /// </summary>
    public static TResult Match<T, TResult>(
        this Result<T> result,
        Func<T, TResult> onSuccess,
        Func<string, TResult> onFailure) =>
        result switch
        {
            Ok<T> ok => onSuccess(ok.Value),
            Fail<T> fail => onFailure(fail.Error),
            _ => onFailure("Unexpected result state.")
        };

    /// <summary>
    /// Asynchronous version of <see cref="Bind{T, U}(Result{T}, Func{T, Result{U}})"/>.
    /// It allows a chain of asynchronous fallible steps to stay a single expression.
    /// </summary>
    public static async Task<Result<U>> BindAsync<T, U>(
        this Result<T> result,
        Func<T, Task<Result<U>>> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (result is Ok<T> ok)
        {
            return await next(ok.Value);
        }

        if (result is Fail<T> fail)
        {
            return CreateFail<U>(fail.Error, fail.Code, fail.Details);
        }

        return CreateFail<U>("Unexpected result state.", "unexpected_state", []);
    }

    /// <summary>
    /// Asynchronous version of <see cref="Map{T, U}(Result{T}, Func{T, U})"/>.
    /// </summary>
    public static async Task<Result<U>> MapAsync<T, U>(
        this Result<T> result,
        Func<T, Task<U>> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (result is Ok<T> ok)
        {
            return new Ok<U>(await next(ok.Value));
        }

        if (result is Fail<T> fail)
        {
            return CreateFail<U>(fail.Error, fail.Code, fail.Details);
        }

        return CreateFail<U>("Unexpected result state.", "unexpected_state", []);
    }

    /// <summary>Discards the value of a result and keeps only success or failure.</summary>
    public static Result ToUnit<T>(this Result<T> result) =>
        result switch
        {
            Ok<T> => Result.Success.Instance,
            Fail<T> fail => new Result.Failure(fail.Error) { Code = fail.Code, Details = fail.Details },
            _ => new Result.Failure("Unexpected result state.")
        };

    /// <summary>Projects a unit result into a typed result.</summary>
    public static Result<T> ToResult<T>(this Result result) =>
        result switch
        {
            Result.Success => new Ok<T>(default!),
            Result.Failure failure => CreateFail<T>(failure.Error, failure.Code, failure.Details),
            _ => CreateFail<T>("Unexpected result state.", "unexpected_state", [])
        };

    /// <summary>Returns the successful values of a sequence of results, ignoring failures.</summary>
    public static IEnumerable<T> SuccessfulValues<T>(this IEnumerable<Result<T>> results) =>
        results.Where(static result => result is Ok<T>).Select(static result => ((Ok<T>)result).Value);

    /// <summary>
    /// Combines two independent results into one result of a pair.
    /// Both of them have to succeed, otherwise the combined result fails. When both of
    /// them failed, the problems of the two of them are kept together, so the interface
    /// can show every problem of the form at once.
    /// </summary>
    public static Result<(TFirst First, TSecond Second)> Combine<TFirst, TSecond>(
        this Result<TFirst> first,
        Result<TSecond> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (first is Ok<TFirst> okFirst && second is Ok<TSecond> okSecond)
        {
            return new Ok<(TFirst, TSecond)>((okFirst.Value, okSecond.Value));
        }

        List<ValidationError> details = [];

        if (first is Fail<TFirst> firstFailure)
        {
            details.AddRange(firstFailure.Details);
        }

        if (second is Fail<TSecond> secondFailure)
        {
            details.AddRange(secondFailure.Details);
        }

        string error = first is Fail<TFirst> failFirst
            ? failFirst.Error
            : second is Fail<TSecond> failSecond
                ? failSecond.Error
                : "Unexpected result state.";

        string code = first is Fail<TFirst> codeFirst
            ? codeFirst.Code
            : second is Fail<TSecond> codeSecond
                ? codeSecond.Code
                : "unexpected_state";

        return new Fail<(TFirst, TSecond)>(error)
        {
            Code = code,
            Details = details
        };
    }

    private static Fail<U> CreateFail<U>(string error, string code, IReadOnlyList<ValidationError> details) =>
        new(error) { Code = code, Details = details };
}
