using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Enums;
using FinanceFlow.Domain.Models;
using FinanceFlow.Domain.Validation;

namespace FinanceFlow.UnitTests;

public sealed class ValidationTests
{
    [Fact]
    public void Required_RejectsAnEmptyValue()
    {
        Result<string> result = Validation.Required("  ", "Name", "The name is required.");

        Assert.True(result.IsFailure());
        Assert.Equal("The name is required.", result.Error);
    }

    [Fact]
    public void Required_TrimsTheValue()
    {
        Result<string> result = Validation.Required("  Finance  ", "Name", "The name is required.");

        Assert.Equal("Finance", result.ValueOr(string.Empty));
    }

    [Fact]
    public void EmailAddress_RejectsAWrongAddress()
    {
        Assert.True(Validation.EmailAddress("not-an-email", "Email").IsFailure());
        Assert.True(Validation.EmailAddress("a@b@c", "Email").IsFailure());
        Assert.True(Validation.EmailAddress("hello@financeflow.app", "Email").IsSuccess());
    }

    [Fact]
    public void EmailAddress_NormalisesTheAddress()
    {
        Result<string> result = Validation.EmailAddress("  Hello@FinanceFlow.app ", "Email");

        Assert.Equal("hello@financeflow.app", result.ValueOr(string.Empty));
    }

    [Theory]
    [InlineData("+1 (415) 555-2671", "+14155552671")]
    [InlineData("+7 777 123 45 67", "+77771234567")]
    [InlineData("777 777 77 77", "+77777777777")]
    public void PhoneNumber_NormalisesInternationalNumbers(string input, string expected)
    {
        Result<string> result = Validation.PhoneNumber(input, "Phone");

        Assert.True(result.IsSuccess());
        Assert.Equal(expected, result.ValueOr(string.Empty));
    }

    [Theory]
    [InlineData("87771234567")]
    [InlineData("+012345678")]
    [InlineData("+123")]
    [InlineData("+1234567890123456")]
    public void PhoneNumber_RejectsNumbersThatAreNotValidE164Inputs(string input)
    {
        Assert.True(Validation.PhoneNumber(input, "Phone").IsFailure());
    }

    [Theory]
    [InlineData("777 777 77 77", "+77777777777")]
    [InlineData("+7 777 777 77 77", "+77777777777")]
    public void KazakhstanPhoneNumber_RequiresAndNormalisesAKazakhstanMobile(string input, string expected)
    {
        Result<string> result = Validation.KazakhstanPhoneNumber(input, "Phone");

        Assert.True(result.IsSuccess());
        Assert.Equal(expected, result.ValueOr(string.Empty));
    }

    [Theory]
    [InlineData("8 777 777 77 77")]
    [InlineData("+1 415 555 2671")]
    [InlineData("+7 600 123 45 67")]
    public void KazakhstanPhoneNumber_RejectsNonKazakhstanOrTrunkPrefixedNumbers(string input)
    {
        Assert.True(Validation.KazakhstanPhoneNumber(input, "Phone").IsFailure());
    }

    [Fact]
    public void Password_RequiresALetterAndADigit()
    {
        Assert.True(Validation.Password("short", "Password").IsFailure());
        Assert.True(Validation.Password("onlyletters", "Password").IsFailure());
        Assert.True(Validation.Password("onlydigits12", "Password").IsSuccess());
        Assert.True(Validation.Password("letters1", "Password").IsSuccess());
    }

    [Fact]
    public void Positive_RejectsZeroAndNegativeNumbers()
    {
        Assert.True(Validation.Positive(0m, "Amount", "Too small.").IsFailure());
        Assert.True(Validation.Positive(-1m, "Amount", "Too small.").IsFailure());
        Assert.True(Validation.Positive(0.01m, "Amount", "Too small.").IsSuccess());
    }

    [Fact]
    public void HexColor_ChecksTheFormat()
    {
        Assert.True(Validation.HexColor("4F46E5", "Color").IsFailure());
        Assert.True(Validation.HexColor("#4F46E5", "Color").IsSuccess());
        Assert.True(Validation.HexColor("#ZZZZZZ", "Color").IsFailure());
    }

    [Fact]
    public void NotInFuture_RejectsAFutureDate()
    {
        DateOnly tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        DateOnly yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        Assert.True(Validation.NotInFuture(tomorrow, "Date", "In the future.").IsFailure());
        Assert.True(Validation.NotInFuture(yesterday, "Date", "In the future.").IsSuccess());
    }

    [Fact]
    public void TransactionValidator_AcceptsAValidDraft()
    {
        TransactionDraft draft = new(Guid.NewGuid(), Guid.NewGuid(), 150.50m, TransactionKind.Expense,
            DateOnly.FromDateTime(DateTime.UtcNow), "Lunch");

        Result<ValidatedTransaction> result = TransactionValidator.Validate(draft);

        Assert.True(result.IsSuccess());
        Assert.Equal(15050, result.ValueOr(default!).Amount.Cents);
    }

    [Fact]
    public void TransactionValidator_RejectsANonPositiveAmount()
    {
        TransactionDraft draft = new(Guid.NewGuid(), Guid.NewGuid(), 0m, TransactionKind.Expense,
            DateOnly.FromDateTime(DateTime.UtcNow), null);

        Result<ValidatedTransaction> result = TransactionValidator.Validate(draft);

        Assert.True(result.IsFailure());
        Assert.Equal("validation", ((Fail<ValidatedTransaction>)result).Code);
    }

    [Fact]
    public void TransactionValidator_RejectsAFutureDate()
    {
        TransactionDraft draft = new(Guid.NewGuid(), Guid.NewGuid(), 10m, TransactionKind.Expense,
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2), null);

        Assert.True(TransactionValidator.Validate(draft).IsFailure());
    }

    [Fact]
    public void TransactionValidator_RejectsATooLongNote()
    {
        TransactionDraft draft = new(Guid.NewGuid(), Guid.NewGuid(), 10m, TransactionKind.Expense,
            DateOnly.FromDateTime(DateTime.UtcNow), new string('x', 300));

        Assert.True(TransactionValidator.Validate(draft).IsFailure());
    }

    [Fact]
    public void GoalValidator_AcceptsAValidDraft()
    {
        GoalDraft draft = new(Guid.NewGuid(), "New laptop", 350_000m,
            DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(3), "For the university");

        Result<ValidatedGoal> result = GoalValidator.Validate(draft);

        Assert.True(result.IsSuccess());
        Assert.Equal(35_000_000, result.ValueOr(default!).TargetAmount.Cents);
    }

    [Fact]
    public void GoalValidator_RejectsAnEmptyTitle()
    {
        GoalDraft draft = new(Guid.NewGuid(), " ", 100m, null, null);

        Assert.True(GoalValidator.Validate(draft).IsFailure());
    }

    [Fact]
    public void GoalValidator_RejectsAPastTargetDate()
    {
        GoalDraft draft = new(Guid.NewGuid(), "Old goal", 100m,
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), null);

        Assert.True(GoalValidator.Validate(draft).IsFailure());
    }

    [Fact]
    public void ValidationBuilder_CollectsEveryProblem()
    {
        ValidationBuilder builder = new ValidationBuilder()
            .Add(Validation.Required(null, "Name", "The name is required."))
            .Add(Validation.Positive(-1m, "Amount", "The amount must be positive."));

        Result<int> result = builder.Failure<int>();

        Assert.True(result.IsFailure());
        Assert.Equal(2, ((Fail<int>)result).Details.Count);
    }
}
