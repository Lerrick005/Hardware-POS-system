namespace Pos.Domain.Tests;

public class QuantityTests
{
    [Fact]
    public void FromWhole_ConvertsToMilli()
    {
        Assert.Equal(5000, Quantity.FromWhole(5).Milli);
    }

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
        Quantity quantity = Quantity.FromMilli(2750);

        Assert.Equal(2, quantity.WholeUnits);
        Assert.Equal(750, quantity.RemainderMilli);
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
}
