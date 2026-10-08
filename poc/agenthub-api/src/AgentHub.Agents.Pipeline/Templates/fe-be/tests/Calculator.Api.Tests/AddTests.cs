namespace Calculator.Api.Tests;

public sealed class AddTests
{
    [Theory]
    [InlineData(2, 3, 5)]
    [InlineData(0, 0, 0)]
    public void Add_works(double a, double b, double expected)
    {
        Assert.Equal(expected, a + b);
    }
}
