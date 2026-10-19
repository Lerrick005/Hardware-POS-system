namespace Pos.Domain;

/// <summary>
/// Quantity in integer thousandths of the base unit (1000 = 1 piece, 1 kg, 1 metre).
/// </summary>
public readonly record struct Quantity : IComparable<Quantity>
{
    public const long MilliPerUnit = 1000;

    public long Milli { get; }

    public Quantity(long milli) => Milli = milli;

    public static Quantity Zero => new(0);

    public static Quantity FromMilli(long milli) => new(milli);

    public static Quantity FromWhole(long units) => new(checked(units * MilliPerUnit));

    public static Quantity FromUnitsAndMilli(long units, long milli) => new(checked(units * MilliPerUnit + milli));

    public bool IsZero => Milli == 0;

    public bool IsNegative => Milli < 0;

    public bool IsPositive => Milli > 0;

    public long WholeUnits => Milli / MilliPerUnit;

    public long RemainderMilli => Milli % MilliPerUnit;

    public static Quantity operator +(Quantity left, Quantity right) => new(checked(left.Milli + right.Milli));

    public static Quantity operator -(Quantity left, Quantity right) => new(checked(left.Milli - right.Milli));

    public static Quantity operator -(Quantity value) => new(checked(-value.Milli));

    public static bool operator <(Quantity left, Quantity right) => left.Milli < right.Milli;

    public static bool operator >(Quantity left, Quantity right) => left.Milli > right.Milli;

    public static bool operator <=(Quantity left, Quantity right) => left.Milli <= right.Milli;

    public static bool operator >=(Quantity left, Quantity right) => left.Milli >= right.Milli;

    public decimal ToDecimalUnits() => Milli / (decimal)MilliPerUnit;

    public int CompareTo(Quantity other) => Milli.CompareTo(other.Milli);

    public override string ToString() =>
        ToDecimalUnits().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
