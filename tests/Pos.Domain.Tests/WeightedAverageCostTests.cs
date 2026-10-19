using Pos.Domain.Stock;

namespace Pos.Domain.Tests;

public class WeightedAverageCostTests
{
    [Fact]
    public void EmptyStock_ReturnsReceivedUnitCost()
    {
        long avg = WeightedAverageCost.Calculate(
            oldQtyMilli: 0, oldAvgCents: 0,
            receivedQtyMilli: 5000, receivedUnitCostCents: 250);

        Assert.Equal(250, avg);
    }

    [Fact]
    public void ZeroAverage_ButPositiveStock_UsesReceivedCost()
    {
        long avg = WeightedAverageCost.Calculate(
            oldQtyMilli: 1000, oldAvgCents: 0,
            receivedQtyMilli: 2000, receivedUnitCostCents: 400);

        Assert.Equal(400, avg);
    }

    [Fact]
    public void SimpleWeightedAverage_RoundsHalfUp()
    {
        // (100*1000 + 200*2000) / 3000 = 500000 / 3000 = 166.666... -> 167
        long avg = WeightedAverageCost.Calculate(
            oldQtyMilli: 1000, oldAvgCents: 100,
            receivedQtyMilli: 2000, receivedUnitCostCents: 200);

        Assert.Equal(167, avg);
    }

    [Fact]
    public void ExactDivision_DoesNotRound()
    {
        // (100*1000 + 200*1000) / 2000 = 300000 / 2000 = 150
        long avg = WeightedAverageCost.Calculate(
            oldQtyMilli: 1000, oldAvgCents: 100,
            receivedQtyMilli: 1000, receivedUnitCostCents: 200);

        Assert.Equal(150, avg);
    }

    [Fact]
    public void FractionalQuantities_Work()
    {
        // old: 2.5 m at 80 cents = 200 cents value
        // received: 1.5 m at 120 cents = 180 cents value
        // new: 4.0 m, value 380 cents, avg 95 cents
        long avg = WeightedAverageCost.Calculate(
            oldQtyMilli: 2500, oldAvgCents: 80,
            receivedQtyMilli: 1500, receivedUnitCostCents: 120);

        Assert.Equal(95, avg);
    }

    [Fact]
    public void NegativeOldStock_IsClampedToZero()
    {
        long avg = WeightedAverageCost.Calculate(
            oldQtyMilli: -3000, oldAvgCents: 100,
            receivedQtyMilli: 2000, receivedUnitCostCents: 500);

        Assert.Equal(500, avg);
    }

    [Fact]
    public void ZeroReceivedQty_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WeightedAverageCost.Calculate(1000, 100, 0, 200));
    }

    [Fact]
    public void NegativeUnitCost_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WeightedAverageCost.Calculate(1000, 100, 1000, -1));
    }

    [Fact]
    public void LargeValues_DoNotOverflow()
    {
        // 10,000 units * 1,000,000 cents each = 10^10
        long avg = WeightedAverageCost.Calculate(
            oldQtyMilli: 10_000_000, oldAvgCents: 1_000_000,
            receivedQtyMilli: 10_000_000, receivedUnitCostCents: 1_000_000);

        Assert.Equal(1_000_000, avg);
    }
}
