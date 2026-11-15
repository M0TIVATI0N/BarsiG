using FinanceFlow.Domain.Common;

namespace FinanceFlow.UnitTests;

public sealed class LazyOperatorsTests
{
    [Fact]
    public void Filter_KeepsOnlyTheMatchingElements()
    {
        List<int> result = new[] { 1, 2, 3, 4, 5 }.Filter(value => value % 2 == 0).ToList();

        Assert.Equal([2, 4], result);
    }

    [Fact]
    public void Transform_ProjectsEveryElement()
    {
        List<string> result = new[] { 1, 2, 3 }.Transform(value => $"#{value}").ToList();

        Assert.Equal(["#1", "#2", "#3"], result);
    }

    [Fact]
    public void Fold_ProducesASingleValue()
    {
        int sum = new[] { 1, 2, 3, 4 }.Fold(0, (accumulator, value) => accumulator + value);

        Assert.Equal(10, sum);
    }

    [Fact]
    public void Head_TakesTheFirstElements()
    {
        List<int> result = new[] { 1, 2, 3, 4 }.Head(2).ToList();

        Assert.Equal([1, 2], result);
    }

    [Fact]
    public void FirstOrFallback_ReturnsTheFallbackForAnEmptySequence()
    {
        int result = Array.Empty<int>().FirstOrFallback(value => value > 0, -1);

        Assert.Equal(-1, result);
    }

    [Fact]
    public void TheOperatorsAreLazy_AndDoNotRunBeforeTheSequenceIsUsed()
    {
        int executions = 0;

        IEnumerable<int> source = new[] { 1, 2, 3 }.Filter(value =>
        {
            executions++;
            return true;
        });

        Assert.Equal(0, executions);

        _ = source.ToList();

        Assert.Equal(3, executions);
    }

    [Fact]
    public void Compose_BuildsANewFunction()
    {
        Func<int, string> composed = Function.Compose<int, int, string>(
            value => value * 2,
            value => $"value: {value}");

        Assert.Equal("value: 10", composed(5));
    }

    [Fact]
    public void ComposeOfThreeFunctions_WorksFromLeftToRight()
    {
        Func<int, string> composed = Function.Compose<int, int, int, string>(
            value => value + 1,
            value => value * 3,
            value => $"result {value}");

        Assert.Equal("result 9", composed(2));
    }
}
