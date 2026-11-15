namespace FinanceFlow.Domain.Common;

/// <summary>
/// A single, human readable problem detected by a pure validator.
/// </summary>
/// <param name="Field">The logical field the problem belongs to.</param>
/// <param name="Message">A short explanation written for the end user.</param>
public sealed record ValidationError(string Field, string Message);

/// <summary>
/// The outcome of an operation that is allowed to fail.
/// The failure is part of the return type instead of an exception,
/// so the caller can never forget to handle it.
/// </summary>
public abstract record Result<T>
{
    private protected Result()
    {
    }

    /// <summary>
    /// The message of a failure, or an empty string when the result is successful.
    /// It lets the interface layer show the problem without a type check.
    /// </summary>
    public abstract string Error { get; }
}

/// <summary>A successful result carrying a value.</summary>
public sealed record Ok<T>(T Value) : Result<T>
{
    /// <inheritdoc />
    public override string Error => string.Empty;
}

/// <summary>A failed result carrying a message and optional structured details.</summary>
public sealed record Fail<T> : Result<T>
{
    /// <summary>Creates a failed result.</summary>
    public Fail(string error)
    {
        Error = error;
    }

    /// <inheritdoc />
    public override string Error { get; }

    /// <summary>Field level details, useful for form validation feedback.</summary>
    public IReadOnlyList<ValidationError> Details { get; init; } = [];

    /// <summary>A stable machine readable code, for example <c>not_found</c>.</summary>
    public string Code { get; init; } = "error";
}

/// <summary>The outcome of an operation that returns no value.</summary>
public abstract record Result
{
    private protected Result()
    {
    }

    /// <summary>The message of a failure, or an empty string when the result is successful.</summary>
    public abstract string Error { get; }

    /// <summary>A successful unit result.</summary>
    public sealed record Success : Result
    {
        private Success()
        {
        }

        /// <summary>The single shared instance of a successful result.</summary>
        public static Success Instance { get; } = new();

        /// <inheritdoc />
        public override string Error => string.Empty;
    }

    /// <summary>A failed unit result.</summary>
    public sealed record Failure : Result
    {
        /// <summary>Creates a failed result.</summary>
        public Failure(string error)
        {
            Error = error;
        }

        /// <inheritdoc />
        public override string Error { get; }

        /// <summary>Field level details, useful for form validation feedback.</summary>
        public IReadOnlyList<ValidationError> Details { get; init; } = [];

        /// <summary>A stable machine readable code, for example <c>not_found</c>.</summary>
        public string Code { get; init; } = "error";
    }
}
