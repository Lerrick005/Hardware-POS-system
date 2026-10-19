namespace Pos.Domain.Stock;

/// <summary>
/// Weighted average cost per whole base unit after a goods receipt.
/// Inputs are all integer: qty in thousandths, cost in cents.
///
/// Rules:
///   * Zero stock -> received unit cost.
///   * avg_cost == 0 with positive stock means "cost was never recorded"
///     (opening stock without cost, freshly created product). The old
///     stock is treated as having zero known value AND excluded from the
///     denominator, so the received cost wins. Counting it would drag the
///     average down and inflate profit reports.
///   * Negative old stock is clamped to zero (invalid state; fresh start).
///   * Formula: (oldAvg*oldQty + newCost*newQty) / (oldQty + newQty), round half up.
/// </summary>
public static class WeightedAverageCost
{
    public static long Calculate(long oldQtyMilli, long oldAvgCents, long receivedQtyMilli, long receivedUnitCostCents)
    {
        if (oldAvgCents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(oldAvgCents), "Average cost cannot be negative.");
        }

        if (receivedQtyMilli <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(receivedQtyMilli), "Received quantity must be positive.");
        }

        if (receivedUnitCostCents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(receivedUnitCostCents), "Received unit cost cannot be negative.");
        }

        long oldStock = Math.Max(0, oldQtyMilli);

        // Unknown-cost old stock: fresh-start on the received cost.
        if (oldAvgCents == 0 || oldStock == 0)
        {
            return receivedUnitCostCents;
        }

        long numerator = checked(oldAvgCents * oldStock + receivedUnitCostCents * receivedQtyMilli);
        long denominator = checked(oldStock + receivedQtyMilli);

        return Money.RoundHalfUpDiv(numerator, denominator);
    }
}
