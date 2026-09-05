using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace PbiBench.Core.Quality;

internal static class SemanticTestValueComparison
{
    // Decimal coefficients preserve Int64 and Decimal values, and the round-trip text of engine doubles.
    // No conversion through Decimal is allowed to silently round subdecimal values down to zero.
    internal static (BigInteger Coefficient, int Exponent) Number(string text)
    {
        if (text.Length > 4096 || !Regex.IsMatch(text, @"^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$", RegexOptions.CultureInvariant)) throw new InvalidDataException("A bounded invariant number is required.");
        var parts = text.ToLowerInvariant().Split('e'); var mantissa = parts[0]; var exponent = 0;
        if (parts.Length == 2 && (!int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent) || Math.Abs((long)exponent) > 4096)) throw new InvalidDataException("The numeric exponent is outside supported bounds.");
        var dot = mantissa.IndexOf('.'); if (dot >= 0) { exponent -= mantissa.Length - dot - 1; mantissa = mantissa.Remove(dot, 1); }
        var coefficient = BigInteger.Parse(mantissa, CultureInfo.InvariantCulture);
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || double.IsNaN(number) || double.IsInfinity(number) || number == 0 && coefficient != BigInteger.Zero)
            throw new InvalidDataException("A finite number representable without numeric underflow is required.");
        return (coefficient, exponent);
    }
    internal static (int Order, bool Equal) CompareNumbers(string actual, string expected, double absoluteTolerance, double relativeTolerance)
    {
        var a = Number(actual); var b = Number(expected); var exponent = Math.Min(a.Exponent, b.Exponent);
        var left = a.Coefficient * BigInteger.Pow(10, a.Exponent - exponent); var right = b.Coefficient * BigInteger.Pow(10, b.Exponent - exponent);
        var order = left.CompareTo(right); if (order == 0) return (0, true);
        if (absoluteTolerance == 0 && relativeTolerance == 0) return (order, false);
        // Compare the exact difference against exact coefficients of the configured tolerances:
        // abs(a-b) <= absoluteTolerance + relativeTolerance * max(abs(a),abs(b)).
        var abs = Number(absoluteTolerance.ToString("R", CultureInfo.InvariantCulture)); var rel = Number(relativeTolerance.ToString("R", CultureInfo.InvariantCulture));
        var maximum = BigInteger.Max(BigInteger.Abs(left), BigInteger.Abs(right));
        var toleranceExponent = Math.Min(exponent, Math.Min(abs.Exponent, rel.Exponent + exponent));
        var difference = BigInteger.Abs(left - right) * BigInteger.Pow(10, exponent - toleranceExponent);
        var threshold = abs.Coefficient * BigInteger.Pow(10, abs.Exponent - toleranceExponent)
            + rel.Coefficient * maximum * BigInteger.Pow(10, rel.Exponent + exponent - toleranceExponent);
        return (order, difference <= threshold);
    }
    internal static (long Ticks, bool Zoned) Date(string value)
    {
        if (!Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}(?:T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})?)?$", RegexOptions.CultureInvariant)) throw new InvalidDataException("An ISO date/time is required.");
        var zoned = value.EndsWith("Z", StringComparison.Ordinal) || Regex.IsMatch(value, @"[+-]\d{2}:\d{2}$", RegexOptions.CultureInvariant);
        if (zoned)
        {
            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var offset)) throw new InvalidDataException("An ISO date/time is required.");
            return (offset.UtcDateTime.Ticks, true);
        }
        if (!DateTime.TryParseExact(value, new[] { "yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw new InvalidDataException("An ISO date/time is required.");
        return (date.Ticks, false);
    }
}
