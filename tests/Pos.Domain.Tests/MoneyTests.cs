namespace Pos.Domain.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData(5, 2, 3)]
    [InlineData(7, 2, 4)]
    [InlineData(1, 2, 1)]
    [InlineData(3, 2, 2)]
    [InlineData(10, 4, 3)]
    [InlineData(0, 5, 0)]
    [InlineData(-5, 2, -3)]
    [InlineData(-7, 2, -4)]
    [InlineData(-1, 2, -1)]
    public void RoundHalfUpDiv_RoundsAwayFromZeroOnExactHalf(long numerator, long denominator, long expected)
    {
        Assert.Equal(expected, Money.RoundHalfUpDiv(numerator, denominator));
    }

    [Fact]
    public void RoundHalfUpDiv_ThrowsOnZeroDenominator()
    {
        Assert.Throws<DivideByZeroException>(() => Money.RoundHalfUpDiv(1, 0));
    }

    [Fact]
    public void AddAndSubtract_AreExact()
    {
        Money total = Money.FromCents(1500) + Money.FromCents(250) - Money.FromCents(75);

        Assert.Equal(1675, total.Cents);
    }

    [Fact]
    public void TimesMilli_RoundsHalfUp()
    {
        Assert.Equal(625, Money.FromCents(250).TimesMilli(2500).Cents);
        Assert.Equal(999, Money.FromCents(333).TimesMilli(3000).Cents);
        Assert.Equal(1, Money.FromCents(1).TimesMilli(500).Cents);
    }

    [Fact]
    public void PercentBp_RoundsHalfUp()
    {
        Assert.Equal(1600, Money.FromCents(10000).PercentBp(1600).Cents);
        Assert.Equal(1975, Money.FromCents(12345).PercentBp(1600).Cents);
        Assert.Equal(198, Money.FromCents(12345).PercentBp(160).Cents);
    }

    [Fact]
    public void Comparison_OrdersByCents()
    {
        Assert.True(Money.FromCents(100) < Money.FromCents(101));
        Assert.True(Money.FromCents(100) <= Money.FromCents(100));
        Assert.True(Money.FromCents(101) > Money.FromCents(100));
    }
}
