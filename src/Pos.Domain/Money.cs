namespace Pos.Domain;

/// <summary>
/// Monetary amount in integer minor units (cents). Floating point is never used for money.
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    public long Cents { get; }

    public Money(long cents) => Cents = cents;

    public static Money Zero => new(0);

    public static Money FromCents(long cents) => new(cents);

    public bool IsZero => Cents == 0;

    public bool IsNegative => Cents < 0;

    public static Money operator +(Money left, Money right) => new(checked(left.Cents + right.Cents));

    public static Money operator -(Money left, Money right) => new(checked(left.Cents - right.Cents));

    public static Money operator -(Money value) => new(checked(-value.Cents));

    public static bool operator <(Money left, Money right) => left.Cents < right.Cents;

    public static bool operator >(Money left, Money right) => left.Cents > right.Cents;

    public static bool operator <=(Money left, Money right) => left.Cents <= right.Cents;

    public static bool operator >=(Money left, Money right) => left.Cents >= right.Cents;

    /// <summary>Multiply by a quantity in thousandths, rounding half up.</summary>
    public Money TimesMilli(long qtyMilli) => new(RoundHalfUpDiv(checked(Cents * qtyMilli), Quantity.MilliPerUnit));

    /// <summary>Apply a rate in basis points (1600 = 16.00 percent), rounding half up.</summary>
    public Money PercentBp(int rateBp) => new(RoundHalfUpDiv(checked(Cents * rateBp), 10_000));

    /// <summary>Integer division with round-half-up (away from zero on exact halves).</summary>
    public static long RoundHalfUpDiv(long numerator, long denominator)
    {
        if (denominator == 0)
        {
            throw new DivideByZeroException("Rounding denominator must not be zero.");
        }

        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        long quotient = numerator / denominator;
        long remainder = numerator % denominator;
        if (remainder == 0)
        {
            return quotient;
        }

        long absRemainder = Math.Abs(remainder);
        if (absRemainder * 2 >= denominator)
        {
            quotient += numerator >= 0 ? 1 : -1;
        }

        return quotient;
    }

    public int CompareTo(Money other) => Cents.CompareTo(other.Cents);

    public override string ToString() =>
        (Cents / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}
