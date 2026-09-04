using System.Collections.ObjectModel;

namespace PbiBench.Dax.LanguageService;

/// <summary>Original concise signature descriptions; function identifiers derive from the MIT TE2 grammar.</summary>
public static class DaxFunctionCatalog
{
    public static IReadOnlyDictionary<string, DaxSignature> BuiltIns { get; } = Create();
    private static IReadOnlyDictionary<string, DaxSignature> Create()
    {
        var result = new Dictionary<string, DaxSignature>(StringComparer.OrdinalIgnoreCase);
        using var stream = typeof(DaxFunctionCatalog).Assembly.GetManifestResourceStream("PbiBench.Dax.LanguageService.DaxFunctions.txt")!;
        using var reader = new System.IO.StreamReader(stream);
        foreach (var name in reader.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            result[name] = new DaxSignature(name, name + " ( … )", Array.Empty<string>(), "DAX built-in function. Open its Microsoft DAX reference for overload details.");
        foreach (var row in Signatures.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = row.Split('|'); var parameters = pieces[1].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            result[pieces[0]] = new DaxSignature(pieces[0], pieces[0] + " ( " + string.Join(", ", parameters) + " )", Array.AsReadOnly(parameters), pieces[2], pieces.Length > 3 ? pieces[3] : null);
        }
        return new ReadOnlyDictionary<string, DaxSignature>(result);
    }
    private const string Signatures = @"SUM|column|Sum a numeric model column.|scalar
SUMX|table;expression|Evaluate an expression for each row and sum its results.|scalar
AVERAGE|column|Average numeric values in a column.|scalar
AVERAGEX|table;expression|Average an expression evaluated for each row.|scalar
MIN|column or expression1;[expression2]|Return the smallest value.|scalar
MAX|column or expression1;[expression2]|Return the largest value.|scalar
MINX|table;expression;[variant]|Evaluate a minimum across rows.|scalar
MAXX|table;expression;[variant]|Evaluate a maximum across rows.|scalar
COUNT|column|Count nonblank values in a column.|scalar
COUNTA|column|Count nonblank column values, including text.|scalar
COUNTROWS|[table]|Count rows in a table expression.|scalar
COUNTX|table;expression|Count nonblank expression results across rows.|scalar
DISTINCTCOUNT|column|Count distinct column values, including blank.|scalar
DISTINCTCOUNTNOBLANK|column|Count distinct nonblank values.|scalar
DIVIDE|numerator;denominator;[alternateResult]|Divide with an alternate result for zero or blank denominators.|scalar
CALCULATE|expression;[filter1];[filter2];…|Evaluate a scalar expression in modified filter context.|scalar
CALCULATETABLE|table;[filter1];[filter2];…|Evaluate a table expression in modified filter context.|table
FILTER|table;filter|Keep rows that satisfy a Boolean expression.|table
ALL|[table or column];[column2];…|Remove filters or return all rows or values.|table
ALLEXCEPT|table;column1;[column2];…|Remove filters except those on the specified columns.|table
ALLSELECTED|[table or column];[column2];…|Return values in the outer query context.|table
REMOVEFILTERS|[table or column];[column2];…|Clear the specified filters.|filter
KEEPFILTERS|expression|Intersect new filters with existing filters.|filter
VALUES|table or column|Return visible distinct rows or values.|table
DISTINCT|table or column|Return distinct rows or values.|table
SELECTEDVALUE|column;[alternateResult]|Return the single visible value or an alternate result.|scalar
HASONEVALUE|column|Test whether one distinct value is visible.|scalar
ISFILTERED|table or column|Test for a direct filter.|scalar
ISINSCOPE|column|Test whether a column is a grouping level.|scalar
RELATED|column|Follow a relationship to obtain a column value.|scalar
RELATEDTABLE|table|Return related rows in the current context.|table
USERELATIONSHIP|column1;column2|Choose the relationship used during evaluation.|filter
CROSSFILTER|column1;column2;direction|Choose relationship filter direction for evaluation.|filter
TREATAS|table;column1;[column2];…|Apply values from a table expression as filters.|table
IF|condition;valueIfTrue;[valueIfFalse]|Choose a result based on a Boolean expression.|scalar
IFERROR|value;valueIfError|Choose an alternate result when evaluation raises an error.|scalar
SWITCH|expression;value1;result1;[value2];[result2];…;[else]|Choose a result by matching an expression.|scalar
COALESCE|expression1;[expression2];…|Return the first nonblank expression.|scalar
BLANK||Return the DAX blank value.|scalar
ISBLANK|value|Test whether a value is blank.|scalar
TRUE||Return true.|scalar
FALSE||Return false.|scalar
ROW|name;expression;[name2];[expression2];…|Build a single-row table.|table
DATATABLE|name;dataType;[name2];[dataType2];…;data|Declare a constant table.|table
ADDCOLUMNS|table;name;expression;[name2];[expression2];…|Append calculated columns to a table expression.|table
SELECTCOLUMNS|table;name;expression;[name2];[expression2];…|Project named expressions from a table.|table
SUMMARIZE|table;[groupByColumn];…;[name];[expression];…|Group rows and optionally add expressions.|table
SUMMARIZECOLUMNS|[groupByColumn];…;[filterTable];…;[name];[expression];…|Group model columns and evaluate expressions.|table
TOPN|rowCount;table;orderByExpression;[order];[orderByExpression2];[order2];…|Return top-ranked rows; ties may add rows.|table
UNION|table1;table2;…|Combine tables with matching column counts.|table
INTERSECT|table1;table2|Return rows present in both tables.|table
EXCEPT|table1;table2|Return rows from the first table absent from the second.|table
CROSSJOIN|table1;table2;…|Return a Cartesian product.|table
GENERATE|table1;table2|Evaluate and combine a second table for each first-table row.|table
GENERATESERIES|startValue;endValue;[increment]|Build a numeric sequence.|table
CALENDAR|startDate;endDate|Build a contiguous date table.|table
CALENDARAUTO|[fiscalYearEndMonth]|Build a date table from model date boundaries.|table
DATE|year;month;day|Construct a date value.|scalar
YEAR|date|Return a date's year.|scalar
MONTH|date|Return a date's month.|scalar
DAY|date|Return a date's day.|scalar
TODAY||Return the current date.|scalar
NOW||Return the current date and time.|scalar
DATEADD|dates or calendar;numberOfIntervals;interval;[extension];[truncation]|Shift a date or calendar selection.|table
DATESYTD|dates or calendar;[yearEndDate]|Return dates in the year to date.|table
TOTALYTD|expression;dates or calendar;[filter];[yearEndDate]|Evaluate an expression over the year to date.|scalar
SAMEPERIODLASTYEAR|dates or calendar|Return the corresponding prior-year period.|table
FORMAT|value;formatString;[localeName]|Format a value as text.|scalar
ROUND|number;digits|Round a numeric value.|scalar
ABS|number|Return the absolute numeric value.|scalar
CONCATENATEX|table;expression;[delimiter];[orderByExpression];[order];…|Join expression results across rows.|scalar
RANKX|table;expression;[value];[order];[ties]|Rank an expression across table rows.|scalar
WINDOW|from;[fromType];to;[toType];[relation];[orderBy];[blanks];[partitionBy];[matchBy];[reset]|Return rows in a window; supported arguments depend on engine version.|table
ORDERBY|expression;[order];[blanks];…|Specify ordering for window functions.|ordering
PARTITIONBY|column;…|Specify partitioning for window functions.|partitioning
MATCHBY|column;…|Identify the current row for window functions.|matching";
}
