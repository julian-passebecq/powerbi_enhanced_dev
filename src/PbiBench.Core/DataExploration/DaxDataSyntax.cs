using System.Globalization;

namespace PbiBench.Core.DataExploration;

/// <summary>Only schema-resolved identifiers and typed literal values enter generated exploration queries.</summary>
public static class DaxDataSyntax
{
    public static string Table(string name) => "'" + Name(name).Replace("'", "''") + "'";
    public static string Column(string table, string column) => Table(table) + "[" + Name(column).Replace("]", "]]") + "]";
    public static string String(string value) => "\"" + (value ?? throw new ArgumentNullException(nameof(value))).Replace("\"", "\"\"") + "\"";
    public static string Literal(string? value, string dataType)
    {
        if (value == null) return "BLANK()";
        switch (dataType.ToLowerInvariant())
        {
            case "string": return String(value);
            case "int64":
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return integer.ToString(CultureInfo.InvariantCulture);
                break;
            case "decimal": case "currency":
                if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number.ToString(CultureInfo.InvariantCulture);
                break;
            case "double":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var real) && !double.IsInfinity(real) && !double.IsNaN(real)) return real.ToString("R", CultureInfo.InvariantCulture);
                break;
            case "boolean":
                if (bool.TryParse(value, out var boolean)) return boolean ? "TRUE()" : "FALSE()";
                break;
            case "datetime": case "date":
                var formats = new[] { "yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm:ss" };
                if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    var result = $"DATE({date.Year}, {date.Month}, {date.Day})";
                    if (date.TimeOfDay != TimeSpan.Zero)
                    {
                        result += $" + TIME({date.Hour}, {date.Minute}, {date.Second})";
                        var fraction = (decimal)(date.Ticks % TimeSpan.TicksPerSecond) / TimeSpan.TicksPerSecond;
                        if (fraction != 0) result += " + " + fraction.ToString(CultureInfo.InvariantCulture) + " / 86400";
                    }
                    return "(" + result + ")";
                }
                break;
            default: throw new ArgumentException($"Filters on data type '{dataType}' are not supported.", nameof(dataType));
        }
        throw new ArgumentException($"'{value}' is not a valid {dataType} value. Use invariant numbers or an ISO date/time.", nameof(value));
    }

    public static string Predicate(DataFilter filter, DataColumnSchema column)
    {
        if (!string.Equals(filter.Column, column.Name, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Filter column does not match the resolved schema.");
        var target = Column(filter.Table, column.Name);
        if (filter.Operator == DataFilterOperator.IsBlank) return "ISBLANK(" + target + ")";
        if (filter.Operator == DataFilterOperator.IsNotBlank) return "NOT(ISBLANK(" + target + "))";
        if (filter.Operator is DataFilterOperator.In or DataFilterOperator.NotIn)
        {
            var values = filter.Values ?? new[] { filter.Value };
            if (values.Count == 0 || values.Count > 500) throw new ArgumentException("Choose between 1 and 500 filter values.");
            var clause = target + " IN { " + string.Join(", ", values.Select(value => Literal(value, column.DataType))) + " }";
            return filter.Operator == DataFilterOperator.NotIn ? "NOT(" + clause + ")" : clause;
        }
        if (filter.Operator is DataFilterOperator.Contains or DataFilterOperator.StartsWith or DataFilterOperator.EndsWith)
        {
            if (!column.DataType.Equals("String", StringComparison.OrdinalIgnoreCase) || filter.Value == null) throw new ArgumentException("Text matching requires a text column and a text value.");
            if (filter.Operator == DataFilterOperator.Contains)
                return "CONTAINSSTRING(" + target + ", " + String(filter.Value.Replace("~", "~~").Replace("*", "~*").Replace("?", "~?")) + ")";
            var value = String(filter.Value);
            return (filter.Operator == DataFilterOperator.StartsWith ? "LEFT(" : "RIGHT(") + target + ", LEN(" + value + ")) == " + value;
        }
        var literal = Literal(filter.Value, column.DataType);
        return filter.Operator switch
        {
            DataFilterOperator.Equals => target + " == " + literal,
            DataFilterOperator.NotEquals => "NOT(" + target + " == " + literal + ")",
            DataFilterOperator.GreaterThan => target + " > " + literal,
            DataFilterOperator.GreaterThanOrEqual => target + " >= " + literal,
            DataFilterOperator.LessThan => target + " < " + literal,
            DataFilterOperator.LessThanOrEqual => target + " <= " + literal,
            _ => throw new ArgumentOutOfRangeException(nameof(filter.Operator))
        };
    }
    private static string Name(string name) => string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A model object name is required.", nameof(name)) : name;
}
