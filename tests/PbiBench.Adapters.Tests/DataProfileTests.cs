using System.Globalization;
using PbiBench.Core.DataExploration;
using PbiBench.Core.Queries;
using Xunit;

namespace PbiBench.Adapters.Tests;

public sealed class DataProfileTests
{
    private static DataTableSchema Table(string type = "Double", string table = "Sales", string column = "Amount", DataStorageMode storage = DataStorageMode.Import)
        => new(table, storage, new[] { new DataColumnSchema(column, type) }, Array.Empty<DataMeasureSchema>(), Array.Empty<string>());

    [Fact]
    public void NumericProfileUsesFullNonblankDataAndGuardsPopulationStdDev()
    {
        var plan = DataProfileBuilder.Column(Table(), "Amount");
        Assert.Contains("FILTER('Sales', NOT(ISBLANK('Sales'[Amount])))", plan.Query);
        Assert.Contains("MEDIANX(__PbiBenchNonBlank, 'Sales'[Amount])", plan.Query);
        Assert.Contains("AVERAGEX(__PbiBenchNonBlank, 'Sales'[Amount])", plan.Query);
        Assert.Contains("IF(__PbiBenchNonBlankCount >= 2, STDEVX.P", plan.Query);
        Assert.Contains("IF(__PbiBenchNonBlankCount = 1, 0, BLANK())", plan.Query);
        Assert.Contains("COUNTROWS(DISTINCT('Sales'[Amount]))", plan.Query);
        Assert.Equal(new[] { "Column summary", "Top values" }, plan.ResultNames);
        Assert.DoesNotContain("PERCENTILEX", plan.Query);
        Assert.True(plan.IsExpensive); Assert.Contains(plan.Warnings, w => w.Contains("display limits do not limit"));
        new QueryRequest("localhost:2383", "Test", plan.Query).Validate();
    }

    [Fact]
    public void AdvancedNumericProfileAddsIqrFencesAndBoundedFrequencySamples()
    {
        var plan = DataProfileBuilder.Column(Table(), "Amount", new DataProfileOptions(7, true));
        Assert.Contains("PERCENTILEX.INC(__PbiBenchNonBlank, 'Sales'[Amount], 0.25)", plan.Query);
        Assert.Contains("PERCENTILEX.INC(__PbiBenchNonBlank, 'Sales'[Amount], 0.75)", plan.Query);
        Assert.Contains("__PbiBenchQ1 - 1.5 * (__PbiBenchQ3 - __PbiBenchQ1)", plan.Query);
        Assert.Contains("__PbiBenchQ3 + 1.5 * (__PbiBenchQ3 - __PbiBenchQ1)", plan.Query);
        Assert.Contains("NOT(ISBLANK([__PbiBenchValue]))", plan.Query);
        Assert.Contains("TOPN(7, FILTER(__PbiBenchFrequency", plan.Query);
        Assert.Contains("IQR outlier values", plan.ResultNames);
        Assert.Contains("ORDER BY [Rows] DESC, [Value] ASC", plan.Query);
        Assert.DoesNotContain("REMOVEFILTERS", plan.Query);
    }

    [Fact]
    public void EscapedIdentifiersAndHelperAliasCollisionCannotChangeTheQueryStructure()
    {
        var table = Table(table: "Owner's table", column: "Amount]x") with
        {
            Columns = new[] { new DataColumnSchema("Amount]x", "Double"), new DataColumnSchema("__PbiBenchGroupCount", "Int64") }
        };
        var plan = DataProfileBuilder.Column(table, "Amount]x");
        Assert.Contains("'Owner''s table'[Amount]]x]", plan.Query);
        Assert.Contains("\"__PbiBenchGroupCount_\", COUNTROWS('Owner''s table')", plan.Query);
        Assert.Contains("[__PbiBenchGroupCount_]", plan.Query);
        Assert.DoesNotContain("\"__PbiBenchGroupCount\", COUNTROWS", plan.Query);
        Assert.Throws<ArgumentException>(() => DataProfileBuilder.Column(table, "not a model column"));
    }

    [Fact]
    public void TextQualityUsesExactTrimComparisonAndExplicitNonbreakingSpaceDetection()
    {
        var plan = DataProfileBuilder.Column(Table("String", column: "Label"), "Label", new DataProfileOptions(12, true));
        Assert.Contains("NOT(EXACT('Sales'[Label], TRIM('Sales'[Label])))", plan.Query);
        Assert.Contains("CONTAINSSTRING('Sales'[Label], UNICHAR(160))", plan.Query);
        Assert.Contains("IFERROR(ISNUMBER(VALUE('Sales'[Label])), FALSE())", plan.Query);
        Assert.Contains("IFERROR(NOT(ISBLANK(DATEVALUE('Sales'[Label]))), FALSE())", plan.Query);
        Assert.Contains("PERCENTILEX.INC(__PbiBenchNonBlank, LEN('Sales'[Label]), 0.25)", plan.Query);
        Assert.Contains("Empty text rows", plan.Query); Assert.Contains("Length outlier rows", plan.Query);
        Assert.Contains(plan.Warnings, warning => warning.Contains("engine locale"));
        Assert.Equal("Whitespace / length candidates", plan.ResultNames.Last());
        Assert.DoesNotContain("STDEVX", plan.Query);
    }

    [Fact]
    public void DateProfileCollapsesTimeOfDayAndFindsActualConsecutiveObservedDayGaps()
    {
        var plan = DataProfileBuilder.Column(Table("DateTime", column: "When"), "When", new DataProfileOptions(5, true));
        Assert.Contains("CONVERT(INT('Sales'[When]), DATETIME)", plan.Query);
        Assert.Contains("VAR __PbiBenchCurrentDay = [__PbiBenchDay] RETURN MAXX(FILTER(__PbiBenchDays, [__PbiBenchDay] < __PbiBenchCurrentDay)", plan.Query);
        Assert.Contains("DATEDIFF([__PbiBenchPreviousDay], [__PbiBenchDay], DAY) - 1", plan.Query);
        Assert.Contains("IF(__PbiBenchDayCount > 0, DATEDIFF(__PbiBenchFirstDay, __PbiBenchLastDay, DAY) + 1 - __PbiBenchDayCount, 0)", plan.Query);
        Assert.Contains("TOPN(5, __PbiBenchGaps", plan.Query);
        Assert.Equal("Largest calendar gaps", plan.ResultNames.Last());
        Assert.Contains(plan.Warnings, warning => warning.Contains("ignoring time of day"));
        Assert.DoesNotContain("CALENDAR(", plan.Query); // No unbounded materialization of every date in the range.
    }

    [Fact]
    public void BasicDateAndTextPlansDoNotSilentlyRunAdvancedParsingOrGapScans()
    {
        var dates = DataProfileBuilder.Column(Table("DateTime"), "Amount");
        var text = DataProfileBuilder.Column(Table("String"), "Amount");
        Assert.DoesNotContain("__PbiBenchGaps", dates.Query); Assert.DoesNotContain("DATEVALUE", text.Query);
        Assert.Equal(2, dates.ResultNames.Count); Assert.Equal(2, text.ResultNames.Count);
    }

    [Fact]
    public void BooleanProfilesAvoidUnsupportedMinxBooleanAndKeepBlankDistinctFromFalse()
    {
        var plan = DataProfileBuilder.Column(Table("Boolean", column: "Flag"), "Flag");
        Assert.Contains("__PbiBenchNonBlank, 'Sales'[Flag] == FALSE()", plan.Query);
        Assert.Contains("__PbiBenchNonBlank, 'Sales'[Flag] == TRUE()", plan.Query);
        Assert.DoesNotContain("MINX", plan.Query); Assert.DoesNotContain("MEDIAN", plan.Query);
        Assert.Throws<ArgumentException>(() => DataProfileBuilder.Column(Table("Binary"), "Amount"));
    }

    [Fact]
    public void RelationshipCoverageUsesRealDistinctValuesAndBothSetDifferenceDirections()
    {
        var (schema, relationship) = Relationship();
        var plan = DataProfileBuilder.Relationship(schema, relationship, new DataProfileOptions(9));
        Assert.Contains("FILTER(DISTINCT('Sales'[ProductKey]), NOT(ISBLANK('Sales'[ProductKey])))", plan.Query);
        Assert.Contains("FILTER(DISTINCT('Product'[Id]), NOT(ISBLANK('Product'[Id])))", plan.Query);
        Assert.Contains("EXCEPT(__PbiBenchFK, __PbiBenchPK)", plan.Query);
        Assert.Contains("EXCEPT(__PbiBenchPK, __PbiBenchFK)", plan.Query);
        Assert.Contains("DIVIDE(__PbiBenchFKCount - __PbiBenchUnmatchedCount, __PbiBenchFKCount)", plan.Query);
        Assert.Contains("DIVIDE(__PbiBenchPKCount - __PbiBenchUnusedCount, __PbiBenchPKCount)", plan.Query);
        Assert.Contains("PK duplicate nonblank rows", plan.Query);
        Assert.Contains("TOPN(9, __PbiBenchUnmatchedFK", plan.Query); Assert.Contains("TOPN(9, __PbiBenchUnusedPK", plan.Query);
        Assert.DoesNotContain("VALUES(", plan.Query); Assert.DoesNotContain("REMOVEFILTERS", plan.Query);
        Assert.Equal(new[] { "Relationship coverage", "Unmatched FK", "Unused PK" }, plan.ResultNames);
        Assert.Contains(plan.Warnings, warning => warning.Contains("denominator is zero"));
    }

    [Fact]
    public void ReversedManyToOneStillUsesTheManySideAsForeignKeys()
    {
        var (schema, relationship) = Relationship();
        var reversed = relationship with { FromTable = "Product", FromColumn = "Id", ToTable = "Sales", ToColumn = "ProductKey", FromCardinality = "One", ToCardinality = "Many" };
        var plan = DataProfileBuilder.Relationship(schema with { Relationships = new[] { reversed } }, reversed);
        Assert.Contains("VAR __PbiBenchFK = FILTER(DISTINCT('Sales'[ProductKey])", plan.Query);
        Assert.Contains("VAR __PbiBenchPK = FILTER(DISTINCT('Product'[Id])", plan.Query);
        Assert.Contains("Unmatched FK", plan.ResultNames);
    }

    [Fact]
    public void ManyToManyAndInactiveRelationshipsDoNotClaimPrimaryKeySemantics()
    {
        var (schema, relationship) = Relationship();
        var many = relationship with { IsActive = false, ToCardinality = "Many" };
        var plan = DataProfileBuilder.Relationship(schema with { Relationships = new[] { many } }, many);
        Assert.Contains("From distinct nonblank", plan.Query); Assert.Contains("To distinct nonblank", plan.Query);
        Assert.DoesNotContain("PK distinct nonblank", plan.Query);
        Assert.Contains(plan.Warnings, warning => warning.Contains("not a many-to-one"));
        Assert.Contains(plan.Warnings, warning => warning.Contains("without activating"));
        Assert.Throws<ArgumentException>(() => DataProfileBuilder.Relationship(schema, many));
    }

    [Fact]
    public void StorageModeAndTypeMismatchCostsRemainExplicitInReviewPlan()
    {
        var direct = DataProfileBuilder.Column(Table(storage: DataStorageMode.DirectQuery), "Amount");
        var lake = DataProfileBuilder.Column(Table(storage: DataStorageMode.DirectLake), "Amount");
        Assert.Contains(direct.Warnings, warning => warning.Contains("source"));
        Assert.Contains(lake.Warnings, warning => warning.Contains("capacity memory"));
        var (schema, relationship) = Relationship();
        var primary = schema.Tables[1] with { Columns = new[] { new DataColumnSchema("Id", "String") } };
        var plan = DataProfileBuilder.Relationship(schema with { Tables = new[] { schema.Tables[0], primary } }, relationship);
        Assert.Contains(plan.Warnings, warning => warning.Contains("without type coercion"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void SampleBoundsAreValidatedBeforeQueryGeneration(int count)
        => Assert.Throws<ArgumentOutOfRangeException>(() => DataProfileBuilder.Column(Table(), "Amount", new DataProfileOptions(count)));

    [Fact]
    public void GeneratedNumericDaxIsCultureIndependentAndLabelsPreserveTypedEngineValues()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var plan = DataProfileBuilder.Column(Table(), "Amount", new DataProfileOptions(3, true));
            Assert.Contains("0.25", plan.Query); Assert.Contains("1.5", plan.Query);
            var set = new QueryResultSet(0, "Result 1", new[] { new QueryColumn("C0", "Rows", "System.Int64") }, new[] { new object?[] { 123L } }, false);
            var result = new QueryResult(Guid.NewGuid(), plan.Query, "server", "model", DateTimeOffset.UtcNow, TimeSpan.Zero, new[] { set }, 1, Array.Empty<string>());
            var labeled = plan.LabelResults(result);
            Assert.Equal("Column summary", labeled.Results[0].Name); Assert.Equal(123L, labeled.Results[0].Rows[0][0]);
            Assert.Same(result.Results[0].Rows, labeled.Results[0].Rows); Assert.Equal("Result 1", result.Results[0].Name);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static (DataModelSchema Schema, DataRelationshipSchema Relationship) Relationship()
    {
        var foreign = Table("Int64", "Sales", "ProductKey"); var primary = Table("Int64", "Product", "Id");
        var relationship = new DataRelationshipSchema("Product sales", "Sales", "ProductKey", "Product", "Id", true);
        return (new DataModelSchema("Example", new[] { foreign, primary }, new[] { relationship }), relationship);
    }
}
