using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Enums;
using FinanceFlow.Domain.Models;

namespace FinanceFlow.Domain.Validation;

/// <summary>
/// Pure validation of a transaction draft.
/// The result of the pipeline is a <see cref="ValidatedTransaction"/>,
/// which is the only input the application layer is allowed to persist.
/// </summary>
public static class TransactionValidator
{
    /// <summary>The maximum length of a free text note.</summary>
    public const int NoteMaxLength = 280;

    /// <summary>Validates a draft and returns the validated stage of the pipeline.</summary>
    public static Result<ValidatedTransaction> Validate(TransactionDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        Result<decimal> amount = Validation.Positive(draft.Amount, "Amount", "The amount must be greater than zero.");
        Result<string> note = Validation.MaxLength(draft.Note, NoteMaxLength, "Note", $"The note must not be longer than {NoteMaxLength} characters.");
        Result<DateOnly> date = Validation.NotInFuture(draft.OccurredOn, "Date", "The transaction date cannot be in the future.");

        ValidationBuilder builder = new ValidationBuilder()
            .Add(amount)
            .Add(note)
            .Add(date);

        if (!builder.IsValid)
        {
            return builder.Failure<ValidatedTransaction>();
        }

        return new Ok<ValidatedTransaction>(new ValidatedTransaction(
            draft.UserId,
            draft.CategoryId,
            Money.FromDecimal(amount.ValueOr(0m)),
            draft.Kind,
            draft.OccurredOn,
            string.IsNullOrWhiteSpace(draft.Note) ? null : draft.Note.Trim()));
    }
}

/// <summary>
/// Pure validation of a savings goal draft.
/// </summary>
public static class GoalValidator
{
    /// <summary>The maximum length of a goal title.</summary>
    public const int TitleMaxLength = 60;

    /// <summary>The maximum length of the note of a goal.</summary>
    public const int NoteMaxLength = 280;

    /// <summary>Validates a draft and returns the validated stage of the pipeline.</summary>
    public static Result<ValidatedGoal> Validate(GoalDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        // The two checks of the title are independent of each other. They are combined
        // into a single result, so both problems reach the interface at the same time.
        Result<(string Title, string Length)> title = Validation.Required(draft.Title, "Title", "The title is required.")
            .Combine(Validation.MaxLength(draft.Title, TitleMaxLength, "Title", $"The title must not be longer than {TitleMaxLength} characters."));

        Result<decimal> target = Validation.Positive(draft.TargetAmount, "TargetAmount", "The target amount must be greater than zero.");

        ValidationBuilder builder = new ValidationBuilder()
            .Add(title)
            .Add(target)
            .Add(Validation.MaxLength(draft.Note, NoteMaxLength, "Note", $"The note must not be longer than {NoteMaxLength} characters."));

        if (draft.TargetDate is { } targetDate)
        {
            builder.Add(Validation.InFuture(targetDate, "TargetDate", "The target date must be in the future."));
        }

        if (!builder.IsValid)
        {
            return builder.Failure<ValidatedGoal>();
        }

        return new Ok<ValidatedGoal>(new ValidatedGoal(
            draft.UserId,
            title.ValueOr((Title: draft.Title, Length: draft.Title)).Title,
            Money.FromDecimal(target.ValueOr(0m)),
            draft.TargetDate,
            string.IsNullOrWhiteSpace(draft.Note) ? null : draft.Note.Trim()));
    }
}
