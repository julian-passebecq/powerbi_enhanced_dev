using Microsoft.VisualStudio.TestTools.UnitTesting;
using PbiBench.Automation;
using PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace PbiBench.Semantic.Tests;

[TestClass]
public class BpaPackTests
{
    [TestMethod]
    public void EightOriginalVersionedPacksHaveStableUniqueRulesAndExplicitRisk()
    {
        Assert.AreEqual(8, BpaRulePacks.BuiltIn.Count);
        Assert.AreEqual(16, BpaRulePacks.Rules.Select(rule => rule.Id).Distinct().Count());
        Assert.IsTrue(BpaRulePacks.BuiltIn.All(pack => pack.Version == "1.0.0" && pack.Origin.Contains("Original")));
        Assert.IsTrue(BpaRulePacks.Rules.All(rule => new[] { "SAFE", "REVIEW", "BENCHMARK", "MANUAL" }.Contains(rule.Risk) && rule.Reference.StartsWith("https://learn.microsoft.com/", StringComparison.Ordinal)));
    }
    [TestMethod]
    public void PackScanIsReadOnlyAndRequiresBenchmarksForPerformanceCandidates()
    {
        using var handler = Model(); var table = handler.Model.Tables["Facts"];
        if (table.Partitions.Count == 0) table.AddMPartition("Data", "let source = #table({\"Value\"},{}) in source");
        foreach (var partition in table.Partitions) partition.Mode = ModeType.Import;
        table.AddCalculatedColumn("Twice", "[Value] * 2").DataType = DataType.Int64;
        var ratio = table.AddMeasure("Ratio", "SUM(Facts[Value]) / COUNTROWS(Facts)");
        var text = table.AddMeasure("Text", "\"/ literal\" // / comment");
        var before = new SemanticModelService(handler).Fingerprint();
        var findings = new BpaService(handler, new AutomationService(handler)).Scan();
        Assert.AreEqual(before, new SemanticModelService(handler).Fingerprint());
        Assert.IsTrue(findings.Any(f => f.RuleId == "PBIBENCH011" && ReferenceEquals(f.Object, ratio)));
        Assert.IsFalse(findings.Any(f => f.RuleId == "PBIBENCH011" && ReferenceEquals(f.Object, text)));
        Assert.IsTrue(findings.Where(f => f.Category == "Performance").All(f => f.Risk == "BENCHMARK" && f.FixPreview == null));
        Assert.IsTrue(findings.Any(f => f.RuleId == "PBIBENCH008"));
        foreach (var partition in table.Partitions) partition.Mode = ModeType.DirectQuery;
        Assert.IsFalse(new BpaService(handler, new AutomationService(handler)).Scan().Any(f => f.RuleId == "PBIBENCH008"));
    }
    [TestMethod]
    public void UserOverridesSuppressOnlyExactRulesAndObjectsWithoutExecutingRuleText()
    {
        using var handler = Model(); var automation = new AutomationService(handler); var service = new BpaService(handler, automation);
        var all = service.Scan(); var description = all.First(f => f.RuleId == "PBIBENCH002");
        var profile = new BpaRuleProfile(); profile.Enabled["PBIBENCH003"] = false;
        profile.Severities["PBIBENCH002"] = FindingSeverity.Error; profile.Suppressions.Add(service.SuppressionKey(description));
        var findings = service.Scan(profile);
        Assert.IsFalse(findings.Any(f => f.RuleId == "PBIBENCH003" || f.RuleId == description.RuleId && f.ObjectPath == description.ObjectPath));
        Assert.IsTrue(findings.Any(f => f.RuleId == "PBIBENCH002" && f.Severity == FindingSeverity.Error));
        profile.Enabled["arbitrary code"] = true; Assert.ThrowsExactly<ArgumentException>(() => service.Scan(profile));
    }
    [TestMethod]
    public void SuppressionDoesNotTransferToAnotherModelIdentity()
    {
        using var handler = Model(); var service = new BpaService(handler, new AutomationService(handler));
        var finding = service.Scan().First(f => f.RuleId == "PBIBENCH002"); var profile = new BpaRuleProfile();
        profile.Suppressions.Add(service.SuppressionKey(finding));
        Assert.IsFalse(service.Scan(profile).Any(f => f.RuleId == finding.RuleId && f.ObjectPath == finding.ObjectPath));
        Assert.IsTrue(service.Scan(profile, includeSuppressed: true).Any(f => f.RuleId == finding.RuleId && f.ObjectPath == finding.ObjectPath));
        handler.Database.ID += "-another-model";
        Assert.IsTrue(service.Scan(profile).Any(f => f.RuleId == finding.RuleId && f.ObjectPath == finding.ObjectPath));
    }
    [TestMethod]
    public void WorkspaceRulesRequireObservedContextAndNeverOfferModelFixes()
    {
        using var handler = Model(); var service = new BpaService(handler, new AutomationService(handler));
        Assert.IsFalse(service.Scan().Any(f => f.Category == "PBIP/Git"));
        var findings = service.Scan(workspace: new BpaWorkspaceContext(true, false, true, true)).Where(f => f.Category == "PBIP/Git").ToArray();
        Assert.AreEqual(3, findings.Length); Assert.IsTrue(findings.All(f => f.Object == null && f.FixPreview == null));
        Assert.IsTrue(findings.Any(f => f.RuleId == "PBIBENCH015" && f.Severity == FindingSeverity.Error));
    }
    [TestMethod]
    public async Task ProfileRoundTripsOverridesAndCanceledWritePreservesOldFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PbiBench-bpa-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "rules.json");
        try
        {
            var profile = new BpaRuleProfile(); profile.Enabled["PBIBENCH007"] = false; await profile.SaveAsync(file, CancellationToken.None);
            var before = File.ReadAllText(file); using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => new BpaRuleProfile().SaveAsync(file, cancel.Token));
            Assert.AreEqual(before, File.ReadAllText(file)); Assert.IsFalse((await BpaRuleProfile.LoadAsync(file, CancellationToken.None)).IsEnabled("PBIBENCH007"));
        }
        finally
        {
            var target = Path.GetFullPath(folder);
            Assert.IsTrue(target.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(Path.GetFileName(target).StartsWith("PbiBench-bpa-", StringComparison.Ordinal));
            Directory.Delete(target, true);
        }
    }
    private static TabularModelHandler Model()
    {
        var handler = new TabularModelHandler(); var table = handler.Model.AddTable("Facts");
        table.AddDataColumn("Value", "Value", dataType: DataType.Int64); table.AddMeasure("Total value", "SUM(Facts[Value])"); table.AddMeasure("Count", "COUNTROWS(Facts)"); return handler;
    }
}
