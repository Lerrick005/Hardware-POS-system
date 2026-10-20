namespace Pos.Domain.Tests;

public class QuantityTests
{
    [Fact] public void FromWhole_ConvertsToMilli() => Assert.Equal(5000, Quantity.FromWhole(5).Milli);

    [Fact]
    public void FromUnitsAndMilli_IsExact()
    {
        Assert.Equal(2500, Quantity.FromUnitsAndMilli(2, 500).Milli);
        Assert.Equal(75, Quantity.FromUnitsAndMilli(0, 75).Milli);
    }

    [Fact]
    public void DecimalUnits_AreReadable()
    {
        Assert.Equal(2.5m, Quantity.FromMilli(2500).ToDecimalUnits());
        Assert.Equal(0.75m, Quantity.FromMilli(750).ToDecimalUnits());
    }

    [Fact]
    public void WholeAndRemainder_SplitCleanly()
    {
        Quantity q = Quantity.FromMilli(2750);
        Assert.Equal(2, q.WholeUnits);
        Assert.Equal(750, q.RemainderMilli);
    }

    [Fact]
    public void IsWhole_Detects_WholeVsFractional()
    {
        Assert.True(Quantity.FromMilli(2000).IsWhole);
        Assert.False(Quantity.FromMilli(2500).IsWhole);
    }

    [Fact]
    public void AddAndSubtract_AreExact()
    {
        Quantity total = Quantity.FromMilli(1500) + Quantity.FromMilli(750) - Quantity.FromMilli(250);
        Assert.Equal(2000, total.Milli);
    }

    [Fact]
    public void Comparison_OrdersByMilli()
    {
        Assert.True(Quantity.FromMilli(1000) < Quantity.FromMilli(1001));
        Assert.True(Quantity.FromMilli(1001) > Quantity.FromMilli(1000));
        Assert.True(Quantity.FromMilli(1000).IsPositive);
        Assert.True(Quantity.Zero.IsZero);
    }

    // ===== TryParse =====

    [Theory]
    [InlineData("2", 2000)]
    [InlineData("1", 1000)]
    [InlineData("0.5", 500)]
    [InlineData("2.5", 2500)]
    [InlineData("0.75", 750)]
    [InlineData("1.234", 1234)]
    [InlineData("0.001", 1)]
    [InlineData("100", 100000)]
    [InlineData(" 3 ", 3000)]
    [InlineData("12.5", 12500)]
    public void TryParse_ValidInput_ReturnsQuantity(string input, long expectedMilli)
    {
        Assert.True(Quantity.TryParse(input, out Quantity q, out string? err), err);
        Assert.Equal(expectedMilli, q.Milli);
        Assert.Null(err);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParse_Empty_Rejected(string? input)
    {
        Assert.False(Quantity.TryParse(input, out _, out string? err));
        Assert.NotNull(err);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("2.5.5")]
    [InlineData("1,2,3")]
    [InlineData("two")]
    public void TryParse_Garbage_Rejected(string input)
    {
        Assert.False(Quantity.TryParse(input, out _, out string? err));
        Assert.NotNull(err);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.000")]
    [InlineData("-1")]
    [InlineData("-2.5")]
    public void TryParse_ZeroOrNegative_Rejected(string input)
    {
        Assert.False(Quantity.TryParse(input, out _, out string? err));
        Assert.Contains("greater than zero", err);
    }

    [Theory]
    [InlineData("2.5001")]
    [InlineData("1.2345")]
    [InlineData("0.0001")]
    public void TryParse_OverPrecision_Rejected(string input)
    {
        Assert.False(Quantity.TryParse(input, out _, out string? err));
        Assert.Contains("3 decimal places", err);
    }

    [Theory]
    [InlineData("2.500")]
    [InlineData("1.230")]
    public void TryParse_TrailingZeros_OK(string input)
    {
        Assert.True(Quantity.TryParse(input, out Quantity q, out _));
        Assert.True(q.Milli > 0);
    }
}
