using FinanceFlow.Domain.Common;

namespace FinanceFlow.UnitTests;

public sealed class ResultTests
{
    [Fact]
    public void Map_ProjectsTheValueOfASuccess()
    {
        Result<int> result = new Ok<int>(21);

        Result<string> mapped = result.Map(value => value.ToString());

        Assert.True(mapped.IsSuccess());
        Assert.Equal("21", mapped.ValueOr(string.Empty));
    }

    [Fact]
    public void Map_PassesAFailureThrough()
    {
        Result<int> result = new Fail<int>("broken");

        Result<string> mapped = result.Map(value => value.ToString());

        Assert.True(mapped.IsFailure());
        Assert.Equal("broken", mapped.Error);
    }

    [Fact]
    public void Bind_ChainsTheSteps()
    {
        Result<int> result = new Ok<int>(2);

        Result<int> bound = result
            .Bind(value => new Ok<int>(value * 3))
            .Bind(value => new Ok<int>(value + 1));

        Assert.Equal(7, bound.ValueOr(0));
    }

    [Fact]
    public void Bind_ShortCircuitsOnTheFirstFailure()
    {
        bool secondStepWasCalled = false;

        Result<int> result = new Fail<int>("stop");

        Result<int> bound = result.Bind(value =>
        {
            secondStepWasCalled = true;
            return new Ok<int>(value);
        });

        Assert.True(bound.IsFailure());
        Assert.False(secondStepWasCalled);
    }

    [Fact]
    public void Match_ChoosesTheContinuation()
    {
        string success = new Ok<int>(5).Match(value => $"ok {value}", error => $"error {error}");
        string failure = new Fail<int>("bad").Match(value => $"ok {value}", error => $"error {error}");

        Assert.Equal("ok 5", success);
        Assert.Equal("error bad", failure);
    }

    [Fact]
    public void ToUnit_KeepsOnlySuccessOrFailure()
    {
        Assert.True(new Ok<int>(1).ToUnit().IsSuccess());
        Assert.True(new Fail<int>("x").ToUnit().IsFailure());
    }

    [Fact]
    public async Task BindAsync_ChainsAsynchronousSteps()
    {
        Result<int> doubled = await new Ok<int>(4)
            .BindAsync(async (int value) => (Result<int>)new Ok<int>(value * 2));

        Result<int> result = await doubled
            .BindAsync(async (int value) => (Result<int>)new Ok<int>(value + 1));

        Assert.Equal(9, result.ValueOr(0));
    }

    [Fact]
    public async Task BindAsync_StopsOnAFailure()
    {
        Result<int> result = await new Ok<int>(4)
            .BindAsync(async (int value) => (Result<int>)new Fail<int>("nope"));

        Assert.True(result.IsFailure());
        Assert.Equal("nope", result.Error);
    }

    [Fact]
    public void SuccessfulValues_SkipsTheFailures()
    {
        List<int> values = new Result<int>[]
        {
            new Ok<int>(1),
            new Fail<int>("x"),
            new Ok<int>(3)
        }.SuccessfulValues().ToList();

        Assert.Equal([1, 3], values);
    }

    [Fact]
    public void ValueOr_ReturnsTheFallbackForAFailure()
    {
        Assert.Equal(7, new Fail<int>("x").ValueOr(7));
        Assert.Equal(7, new Ok<int>(7).ValueOr(9));
    }
}
