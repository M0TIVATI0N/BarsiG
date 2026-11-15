using FinanceFlow.Domain.Common;

namespace FinanceFlow.Domain.Validation;

/// <summary>
/// A collection of small, pure validation primitives.
/// Every function takes a value and returns a result: it never throws and never mutates anything,
/// which makes the rules trivial to unit test and safe to reuse.
/// </summary>
public static class Validation
{
    /// <summary>Requires a non empty, non whitespace value.</summary>
    public static Result<string> Required(string? value, string field, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Invalid<string>(message, field, message);
        }

        return new Ok<string>(value.Trim());
    }

    /// <summary>Requires a minimum number of characters.</summary>
    public static Result<string> MinLength(string? value, int minimum, string field, string message)
    {
        if (value is null || value.Trim().Length < minimum)
        {
            return Invalid<string>(message, field, message);
        }

        return new Ok<string>(value.Trim());
    }

    /// <summary>Requires a maximum number of characters.</summary>
    public static Result<string> MaxLength(string? value, int maximum, string field, string message)
    {
        if (value is not null && value.Trim().Length > maximum)
        {
            return Invalid<string>(message, field, message);
        }

        return new Ok<string>(value?.Trim() ?? string.Empty);
    }

    /// <summary>Requires a syntactically valid e-mail address.</summary>
    public static Result<string> EmailAddress(string? value, string field)
    {
        Result<string> notEmpty = Required(value, field, "E-mail address is required.");
        if (notEmpty.IsFailure())
        {
            return notEmpty;
        }

        string candidate = notEmpty.ValueOr(string.Empty);
        if (!candidate.Contains('@') || candidate.StartsWith('@') || candidate.EndsWith('@') ||
            candidate.Contains(' ') || candidate.Count(c => c == '@') != 1)
        {
            return Invalid<string>("Enter a valid e-mail address.", field, "Enter a valid e-mail address.");
        }

        return new Ok<string>(candidate.ToLowerInvariant());
    }

    /// <summary>
    /// Normalises an international phone number to E.164. Kazakhstan mobile numbers may
    /// also be entered in their national form, without a country prefix.
    /// </summary>
    public static Result<string> PhoneNumber(string? value, string field)
    {
        const string message = "Enter a valid international phone number or a 10-digit Kazakhstan mobile number.";

        if (string.IsNullOrWhiteSpace(value))
        {
            return Invalid<string>("Phone number is required.", field, "Phone number is required.");
        }

        string candidate = value.Trim();

        bool hasInternationalPrefix = candidate.StartsWith('+');
        string digits = new(candidate
            .Skip(hasInternationalPrefix ? 1 : 0)
            .Where(character => character is not (' ' or '-' or '(' or ')' or '.'))
            .ToArray());

        if (!hasInternationalPrefix)
        {
            if (digits.Length != 10 ||
                digits[0] != '7' ||
                digits.Any(character => !char.IsAsciiDigit(character)))
            {
                return Invalid<string>(message, field, message);
            }

            digits = $"7{digits}";
        }

        if (digits.Length is < 8 or > 15 ||
            digits[0] == '0' ||
            digits.Any(character => !char.IsAsciiDigit(character)))
        {
            return Invalid<string>(message, field, message);
        }

        return new Ok<string>($"+{digits}");
    }

    /// <summary>
    /// Normalises a Kazakhstan mobile phone number to E.164. Accepts either a national
    /// 10-digit number such as <c>777 777 77 77</c> or its <c>+7</c> form.
    /// </summary>
    public static Result<string> KazakhstanPhoneNumber(string? value, string field)
    {
        const string message = "Enter a Kazakhstan mobile number, for example 777 777 77 77.";
        Result<string> normalized = PhoneNumber(value, field);

        if (normalized.IsFailure())
        {
            return Invalid<string>(message, field, message);
        }

        string candidate = normalized.ValueOr(string.Empty);
        if (candidate.Length != 12 || !candidate.StartsWith("+77", StringComparison.Ordinal))
        {
            return Invalid<string>(message, field, message);
        }

        return new Ok<string>(candidate);
    }

    /// <summary>
    /// Requires a password of at least eight characters that contains a letter and a digit.
    /// </summary>
    public static Result<string> Password(string? value, string field)
    {
        Result<string> notEmpty = Required(value, field, "Password is required.");
        if (notEmpty.IsFailure())
        {
            return notEmpty;
        }

        string candidate = notEmpty.ValueOr(string.Empty);
        if (candidate.Length < 8)
        {
            return Invalid<string>("The password must contain at least 8 characters.", field, "The password must contain at least 8 characters.");
        }

        if (!candidate.Any(char.IsLetter) || !candidate.Any(char.IsDigit))
        {
            return Invalid<string>("The password must contain a letter and a digit.", field, "The password must contain a letter and a digit.");
        }

        return new Ok<string>(candidate);
    }

    /// <summary>Requires a strictly positive amount.</summary>
    public static Result<decimal> Positive(decimal value, string field, string message)
    {
        if (value <= 0m)
        {
            return Invalid<decimal>(message, field, message);
        }

        return new Ok<decimal>(value);
    }

    /// <summary>Requires an amount of zero or more.</summary>
    public static Result<decimal> NotNegative(decimal value, string field, string message)
    {
        if (value < 0m)
        {
            return Invalid<decimal>(message, field, message);
        }

        return new Ok<decimal>(value);
    }

    /// <summary>Requires a date that is not in the future.</summary>
    public static Result<DateOnly> NotInFuture(DateOnly value, string field, string message)
    {
        if (value > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return Invalid<DateOnly>(message, field, message);
        }

        return new Ok<DateOnly>(value);
    }

    /// <summary>Requires a date that is in the future.</summary>
    public static Result<DateOnly> InFuture(DateOnly value, string field, string message)
    {
        if (value <= DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return Invalid<DateOnly>(message, field, message);
        }

        return new Ok<DateOnly>(value);
    }

    /// <summary>Requires a hexadecimal colour such as <c>#4F46E5</c>.</summary>
    public static Result<string> HexColor(string? value, string field)
    {
        string candidate = value?.Trim() ?? string.Empty;
        bool valid = candidate.Length == 7 && candidate[0] == '#' &&
                     candidate[1..].All(Uri.IsHexDigit);

        if (!valid)
        {
            return Invalid<string>("The colour must look like #4F46E5.", field, "The colour must look like #4F46E5.");
        }

        return new Ok<string>(candidate.ToUpperInvariant());
    }

    /// <summary>
    /// Runs a set of checks of the same type and collects every failure into a single result.
    /// The caller therefore sees all problems at once instead of fixing them one by one.
    /// </summary>
    public static Result<T> Collect<T>(params Func<Result<T>>[] checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        List<ValidationError> errors = [];
        T? value = default;
        bool succeeded = true;

        foreach (Func<Result<T>> check in checks)
        {
            Result<T> result = check();

            if (result is Ok<T> ok)
            {
                value = ok.Value;
            }
            else if (result is Fail<T> fail)
            {
                succeeded = false;
                errors.AddRange(fail.Details);
            }
        }

        if (succeeded)
        {
            return new Ok<T>(value!);
        }

        return new Fail<T>(errors.Count > 0 ? errors[0].Message : "The submitted data is not valid.")
        {
            Code = "validation",
            Details = errors
        };
    }

    private static Result<T> Invalid<T>(string message, string field, string detail) =>
        new Fail<T>(message)
        {
            Code = "validation",
            Details = [new ValidationError(field, detail)]
        };
}

/// <summary>
/// A small accumulator that collects every validation problem of a form into one result.
/// The caller sees all problems at once, which is exactly what a user interface needs.
/// </summary>
public sealed class ValidationBuilder
{
    private readonly List<ValidationError> _errors = [];

    /// <summary>All problems collected so far.</summary>
    public IReadOnlyList<ValidationError> Errors => _errors;

    /// <summary><c>true</c> when no problem has been collected.</summary>
    public bool IsValid => _errors.Count == 0;

    /// <summary>Adds the problems of a failed result and ignores a successful one.</summary>
    public ValidationBuilder Add<T>(Result<T> result)
    {
        if (result is Fail<T> fail)
        {
            _errors.AddRange(fail.Details.Count > 0
                ? fail.Details
                : [new ValidationError(string.Empty, fail.Error)]);
        }

        return this;
    }

    /// <summary>Adds a single problem.</summary>
    public ValidationBuilder AddError(string field, string message)
    {
        _errors.Add(new ValidationError(field, message));
        return this;
    }

    /// <summary>Builds a failed result that carries every collected problem.</summary>
    public Result<T> Failure<T>() =>
        new Fail<T>(_errors.Count > 0 ? _errors[0].Message : "The submitted data is not valid.")
        {
            Code = "validation",
            Details = [.. _errors]
        };
}
