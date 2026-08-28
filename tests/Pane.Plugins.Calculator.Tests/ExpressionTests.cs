using Pane.Plugins.Calculator;
using Xunit;

public class ExpressionTests
{
    [Theory]
    [InlineData("2+2", 4)]
    [InlineData("2 * (3 + 4)", 14)]
    [InlineData("10 / 4", 2.5)]
    [InlineData("-3 + 5", 2)]
    [InlineData("2 * -3", -6)]
    [InlineData("-(3 + 4)", -7)]
    [InlineData("2 * (3 + -4)", -2)]
    [InlineData("+3", 3)]
    [InlineData("2 * +3", 6)]
    public void Evaluates_valid_expressions(string input, double expected)
    {
        Assert.True(Expression.TryEval(input, out var r));
        Assert.Equal(expected, r, 3);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("2 +")]
    [InlineData("")]
    [InlineData("1/0")]
    [InlineData("(2+3")]
    public void Rejects_non_expressions(string input)
    {
        Assert.False(Expression.TryEval(input, out _));
    }
}
