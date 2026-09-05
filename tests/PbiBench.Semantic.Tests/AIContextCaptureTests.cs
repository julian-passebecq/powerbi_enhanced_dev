using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PbiBench.AI.ContextExport;
using PbiBench.Core.Queries;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.Tests;
[TestClass]
public sealed class AIContextCaptureTests
{
    [TestMethod]
    public async Task CaptureExcludesSourcePropertiesAndPreservesSemanticContextWithoutMutation()
    {
        using var handler = new TabularModelHandler(1702);
        var table = handler.Model.AddTable("Sales"); table.AddDataColumn("Amount", "SOURCE_COLUMN_SECRET", dataType: DataType.Decimal);
        table.Partitions[0].Expression = "let source = \"PARTITION_SECRET\" in source";
        table.SetAnnotation("Secret", "ANNOTATION_SECRET");
        var source = handler.Model.AddDataSource("Private source"); source.ConnectionString = "Provider=MSOLEDBSQL;Password=CONNECTION_SECRET;";
        var function = handler.Model.AddFunction("Fixture.Double"); function.Expression = "(value: SCALAR INT64) => value * 2";
        var measure = table.AddMeasure("Revenue", "SUM('Sales'[Amount])"); measure.Description = "Gross amount";
        var group = handler.Model.AddCalculationGroup("Time"); group.AddCalculationItem("Current", "SELECTEDMEASURE()");
        var role = handler.Model.AddRole("Restricted"); table.RowLevelSecurity[role] = "[Amount] > 10";
        var perspective = handler.Model.AddPerspective("Finance"); measure.InPerspective[perspective] = true;
        handler.Model.AddTranslation("fr-FR"); measure.TranslatedNames["fr-FR"] = "Revenu";
        var before = new SemanticModelService(handler).Fingerprint();
        var captured = AIContextCapture.Capture(handler, true); var plan = await ContextExporter.PrepareAsync(captured, new() { IncludeRoles = true }, null, default);
        var content = string.Join("\n", plan.Review.Select(f => plan.ReadText(f.Path)));
        foreach (var secret in new[] { "SOURCE_COLUMN_SECRET", "PARTITION_SECRET", "ANNOTATION_SECRET", "CONNECTION_SECRET" }) Assert.IsFalse(content.Contains(secret), secret);
        Assert.IsTrue(plan.ReadText("model/functions.dax").Contains("Fixture.Double"));
        Assert.IsTrue(plan.ReadText("model/calculation-groups.json").Contains("SELECTEDMEASURE")); Assert.IsTrue(plan.ReadText("model/translations.json").Contains("Revenu")); Assert.IsTrue(plan.ReadText("model/perspectives.json").Contains("Finance"));
        Assert.IsTrue(plan.ReadText("model/roles.json").Contains("Amount")); Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
        Assert.IsTrue(captured.Dependencies.Any(d => d.ObjectId == AIContextCapture.Id(measure)));
        Assert.AreEqual(0, AIContextCapture.Capture(handler).Roles.Count);
    }
    [TestMethod]
    public async Task SampleProjectionQuotesIdentifiersAndDoesNotSerializeConnection()
    {
        var query = new Queries(); var sampler = new SemanticContextSampler(query, "fixture", "fixture", "Password=SECRET_CONNECTION");
        var result = await sampler.SampleAsync(new("Odd'Table", new[] { "a]b" }, 5), default);
        Assert.IsTrue(query.Request!.Query.Contains("'Odd''Table'[a]]b]")); Assert.IsTrue(query.Request.Query.Contains("SELECTCOLUMNS")); Assert.AreEqual(5, query.Request.RowLimit);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("SECRET_CONNECTION"));
    }
    private sealed class Queries : IDaxQueryService
    {
        public QueryRequest? Request;
        public Task<QueryResult> ExecuteAsync(QueryRequest request, CancellationToken cancellationToken)
        { Request = request; return Task.FromResult(new QueryResult(Guid.NewGuid(), request.Query, request.Server, request.Database, DateTimeOffset.UtcNow, TimeSpan.Zero, new[] { new QueryResultSet(0, "Fixture", new[] { new QueryColumn("C0", "C0", "Decimal") }, new[] { new object?[] { 1m } }, false) }, 0, Array.Empty<string>())); }
    }
}
