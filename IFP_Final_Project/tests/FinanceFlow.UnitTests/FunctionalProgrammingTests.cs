using FinanceFlow.Domain.Analytics;
using FinanceFlow.Domain.Banking;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Entities;
using FinanceFlow.Domain.Enums;
using FinanceFlow.Domain.Models;
using FinanceFlow.Domain.Validation;

namespace FinanceFlow.UnitTests;

/// <summary>
/// Proves the functional parts of BarsiG: the composition of the functions, the curried
/// predicates, the pipe, the combination of two results, the pure validation and the
/// recursive projection of a deposit.
/// </summary>
public sealed class FunctionalProgrammingTests
{
    private static Transaction Build(
        TransactionKind kind,
        decimal amount,
        DateOnly occurredOn,
        bool recurring = false,
        string? note = null,
        Guid? categoryId = null) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            categoryId ?? Guid.NewGuid(),
            Money.FromDecimal(amount),
            kind,
            occurredOn,
            note,
            recurring,
            DateTimeOffset.UtcNow);

    // ------------------------------------------------------------------- pipe

    [Fact]
    public void Pipe_ThreadsTheValueThroughTheChainFromLeftToRight()
    {
        int result = Function.Pipe(
            21,
            static value => value * 2,
            static value => value + 3,
            static value => value - 1);

        Assert.Equal(44, result);
    }

    [Fact]
    public void Pipe_WithOneFunctionIsThePlainApplication()
    {
        string result = Function.Pipe("barsig", static value => value.ToUpperInvariant());

        Assert.Equal("BARSIG", result);
    }

    // ------------------------------------------------------------------ curry

    [Fact]
    public void Curry_AppliesTheFirstArgumentNowAndTheSecondOneLater()
    {
        Func<int, int> addFive = Function.Curry(static (int left, int right) => left + right, 5);

        Assert.Equal(7, addFive(2));
    }

    [Fact]
    public void Curry_WithThreeArgumentsKeepsTheFirstOne()
    {
        Func<int, int, int> subtract = Function.Curry(static (int a, int b, int c) => a - b - c, 10);

        Assert.Equal(4, subtract(3, 3));
    }

    [Fact]
    public void Flip_IsTheSameOperationAsCurry()
    {
        Func<string, string> flip = Function.Flip(static (int number, string name) => $"{name}:{number}", 7);

        Assert.Equal("seven:7", flip("seven"));
    }

    [Fact]
    public void CurriedPredicate_SelectsTheTransactionsOfOneKind()
    {
        Transaction income = Build(TransactionKind.Income, 100m, new DateOnly(2026, 1, 5));
        Transaction expense = Build(TransactionKind.Expense, 40m, new DateOnly(2026, 1, 6));

        List<Transaction> transactions = [income, expense];

        Money total = TransactionClassifier.Total(transactions, TransactionClassifier.OfKind(TransactionKind.Income));

        Assert.Equal(Money.FromDecimal(100m), total);
    }

    // ---------------------------------------------------------------- combine

    [Fact]
    public void Combine_SucceedsWhenBothResultsSucceed()
    {
        Result<string> first = new Ok<string>("a");
        Result<int> second = new Ok<int>(2);

        Result<(string First, int Second)> combined = first.Combine(second);

        Assert.True(combined.IsSuccess());
        Assert.Equal(("a", 2), combined.ValueOr((string.Empty, 0)));
    }

    [Fact]
    public void Combine_FailsWhenOneOfTheResultsFails()
    {
        Result<string> first = new Ok<string>("a");
        Result<int> second = new Fail<int>("The second one failed.") { Code = "second" };

        Result<(string First, int Second)> combined = first.Combine(second);

        Assert.True(combined.IsFailure());
        Assert.Equal("The second one failed.", combined.Error);
        Assert.Equal("second", ((Fail<(string, int)>)combined).Code);
    }

    [Fact]
    public void Combine_KeepsTheProblemsOfBothResults()
    {
        Result<string> first = new Fail<string>("The first problem.")
        {
            Details = [new ValidationError("Title", "The first problem.")]
        };

        Result<int> second = new Fail<int>("The second problem.")
        {
            Details = [new ValidationError("Note", "The second problem.")]
        };

        Result<(string First, int Second)> combined = first.Combine(second);

        Assert.True(combined.IsFailure());
        Assert.Equal(2, ((Fail<(string, int)>)combined).Details.Count);
    }

    // ------------------------------------------------------- pure validation

    [Fact]
    public void ValidatedTransaction_CanOnlyBeBuiltFromAValidDraft()
    {
        TransactionDraft good = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            250m,
            TransactionKind.Expense,
            new DateOnly(2026, 1, 5),
            "  Groceries  ");

        Result<ValidatedTransaction> result = TransactionValidator.Validate(good);

        Assert.True(result.IsSuccess());
        Assert.Equal("Groceries", result.ValueOr(default!).Note);
    }

    [Fact]
    public void ValidatedTransaction_ReportsEveryProblemOfTheDraft()
    {
        TransactionDraft bad = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            0m,
            TransactionKind.Expense,
            new DateOnly(2030, 1, 5),
            null);

        Result<ValidatedTransaction> result = TransactionValidator.Validate(bad);

        Assert.True(result.IsFailure());
        Assert.Equal(2, ((Fail<ValidatedTransaction>)result).Details.Count);
    }

    [Fact]
    public void GoalValidator_CombinesTheTwoChecksOfTheTitle()
    {
        GoalDraft draft = new(Guid.NewGuid(), new string('x', 80), 500m, null, new string('y', 400));

        Result<ValidatedGoal> result = GoalValidator.Validate(draft);

        Assert.True(result.IsFailure());
        Assert.Equal(2, ((Fail<ValidatedGoal>)result).Details.Count);
    }

    // -------------------------------------------------------- lazy operators

    [Fact]
    public void LazyOperators_FilterAndTransformAreLazyAndKeepTheOrder()
    {
        int[] numbers = [1, 2, 3, 4, 5, 6];

        IEnumerable<int> evens = numbers
            .Filter(static number => number % 2 == 0)
            .Transform(static number => number * 10);

        Assert.Equal([20, 40, 60], evens.ToList());
    }

    [Fact]
    public void LazyOperators_FoldNeverMutatesTheAccumulator()
    {
        int[] numbers = [1, 2, 3, 4];

        int sum = numbers.Fold(0, static (accumulator, number) => accumulator + number);

        Assert.Equal(10, sum);
    }

    [Fact]
    public void LazyOperators_FirstOrFallbackIsSafeOnAnEmptySequence()
    {
        int[] empty = [];

        int result = empty.FirstOrFallback(static number => number > 0, -1);

        Assert.Equal(-1, result);
    }

    // ------------------------------------------------------- the classifier

    [Fact]
    public void TheClassifier_FindsTheBiggestTransaction()
    {
        List<Transaction> transactions =
        [
            Build(TransactionKind.Expense, 10m, new DateOnly(2026, 1, 5)),
            Build(TransactionKind.Expense, 990m, new DateOnly(2026, 1, 6)),
            Build(TransactionKind.Expense, 50m, new DateOnly(2026, 1, 7))
        ];

        Assert.Equal(Money.FromDecimal(990m), TransactionClassifier.Largest(transactions));
    }

    [Fact]
    public void TheClassifier_HandlesAnEmptySequence()
    {
        Assert.Equal(Money.Zero, TransactionClassifier.Largest([]));
    }

    [Fact]
    public void TheClassifier_CountsTheTransactionsOfEverySize()
    {
        Guid category = Guid.NewGuid();

        List<Transaction> transactions =
        [
            Build(TransactionKind.Expense, 5m, new DateOnly(2026, 1, 5), categoryId: category),
            Build(TransactionKind.Expense, 200m, new DateOnly(2026, 1, 6), categoryId: category),
            Build(TransactionKind.Expense, 900m, new DateOnly(2026, 1, 7), categoryId: category)
        ];

        IReadOnlyDictionary<TransactionSize, int> counts = TransactionClassifier.CountBySize(transactions);

        Assert.Equal(1, counts[TransactionSize.Small]);
        Assert.Equal(1, counts[TransactionSize.Medium]);
        Assert.Equal(1, counts[TransactionSize.Large]);
    }

    [Fact]
    public void TheClassifier_CountsTheTransactionsThatRepeatThemselves()
    {
        Guid category = Guid.NewGuid();

        List<Transaction> transactions =
        [
            Build(TransactionKind.Expense, 50m, new DateOnly(2026, 1, 5), true, "Rent", category),
            Build(TransactionKind.Expense, 50m, new DateOnly(2026, 2, 5), true, "Rent", category),
            Build(TransactionKind.Expense, 50m, new DateOnly(2026, 3, 5), true, "Rent", category),
            Build(TransactionKind.Expense, 10m, new DateOnly(2026, 4, 5), true, "Coffee", category)
        ];

        Assert.Equal(1, TransactionClassifier.CountRecurring(transactions));
    }

    // ------------------------------------------------------------- recursion

    [Fact]
    public void TheProjectionOfADeposit_GrowsWithEveryMonth()
    {
        Money principal = Money.FromDecimal(1000m);

        Money afterZero = BankingMath.ProjectBalance(principal, 12m, 0);
        Money afterOne = BankingMath.ProjectBalance(principal, 12m, 1);
        Money afterTwelve = BankingMath.ProjectBalance(principal, 12m, 12);

        Assert.Equal(principal, afterZero);
        Assert.True(afterOne > principal);
        Assert.True(afterTwelve > afterOne);
    }

    [Fact]
    public void TheProjectionOfADeposit_IsTheSameEveryTime()
    {
        Money first = BankingMath.ProjectBalance(Money.FromDecimal(2500m), 9.5m, 18);
        Money second = BankingMath.ProjectBalance(Money.FromDecimal(2500m), 9.5m, 18);

        Assert.Equal(first, second);
    }

    [Fact]
    public void TheProjectionOfADeposit_KeepsAnEmptyDepositEmpty()
    {
        Assert.Equal(Money.Zero, BankingMath.ProjectBalance(Money.Zero, 12m, 24));
    }
}
