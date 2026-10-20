using System.Globalization;

namespace Pos.Domain;

/// <summary>
/// Quantity in integer thousandths of the base unit (1000 = 1 piece, 1 kg, 1 metre).
/// </summary>
public readonly record struct Quantity : IComparable<Quantity>
{
    public const long MilliPerUnit = 1000;

    /// <summary>
    /// Strict numeric styles for user input: whitespace, sign, decimal point.
    /// Deliberately excludes thousands separators — "1,2,3" must not silently
    /// become 123. Quantities are rarely in the thousands; if a user needs
    /// 1000+ they should type "1000" without commas.
    /// </summary>
    private const NumberStyles AcceptedStyles =
        NumberStyles.AllowLeadingWhite
        | NumberStyles.AllowTrailingWhite
        | NumberStyles.AllowLeadingSign
        | NumberStyles.AllowDecimalPoint;

    public long Milli { get; }

    public Quantity(long milli) => Milli = milli;

    public static Quantity Zero => new(0);

    public static Quantity FromMilli(long milli) => new(milli);

    public static Quantity FromWhole(long units) => new(checked(units * MilliPerUnit));

    public static Quantity FromUnitsAndMilli(long units, long milli) => new(checked(units * MilliPerUnit + milli));

    /// <summary>
    /// Parses user-entered quantity text. Accepts whole ("2") and fractional
    /// ("2.5", "0.75") values up to 3 decimal places. Rejects zero, negatives,
    /// garbage, and over-precision (avoids silent rounding of a number the user
    /// didn't intend).
    ///
    /// The error message is shop-language, safe to display directly (ADR-0010).
    /// </summary>
    public static bool TryParse(string? input, out Quantity result, out string? error)
    {
        result = Zero;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Enter a quantity.";
            return false;
        }

        if (!decimal.TryParse(input.Trim(), AcceptedStyles, CultureInfo.InvariantCulture, out decimal value))
        {
            error = "Enter a valid number (e.g. 2, 2.5, 0.75).";
            return false;
        }

        if (value <= 0)
        {
            error = "Quantity must be greater than zero.";
            return false;
        }

        // Reject over-precision. Silent rounding of "2.5001" to "2.500" would
        // be a lie to the cashier who typed it. Be honest: ask for 3 decimals.
        decimal rounded = decimal.Round(value, 3, MidpointRounding.AwayFromZero);
        if (rounded != value)
        {
            error = "Up to 3 decimal places allowed (e.g. 2.5, 0.75, 1.234).";
            return false;
        }

        long milli = (long)decimal.Round(value * MilliPerUnit, 0, MidpointRounding.AwayFromZero);
        result = new Quantity(milli);
        return true;
    }

    public bool IsZero => Milli == 0;
    public bool IsNegative => Milli < 0;
    public bool IsPositive => Milli > 0;

    public long WholeUnits => Milli / MilliPerUnit;
    public long RemainderMilli => Milli % MilliPerUnit;

    public bool IsWhole => Milli % MilliPerUnit == 0;

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
        ToDecimalUnits().ToString("0.###", CultureInfo.InvariantCulture);
}

