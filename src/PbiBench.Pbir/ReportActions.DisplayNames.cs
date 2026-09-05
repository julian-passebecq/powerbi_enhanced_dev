namespace PbiBench.Pbir;

public sealed partial class ReportActions
{
    public ReportChangePlan ApplyDisplayNames(ReportIndex report, DisplayNameManifest manifest)
    {
        var rows = new List<ReportFileChange>(); var matched = new HashSet<DisplayNameMapping>();
        foreach (var page in report.Pages) foreach (var visual in page.Visuals)
        {
            var before = report.Files[visual.File]; var json = before.Json(); var changed = false;
            foreach (var projection in DisplayNameManifest.Projections(json))
            {
                var mapping = manifest.Mappings.SingleOrDefault(m => m.Report == report.Name && m.Page == page.Id && m.Visual == visual.Id && m.Field == projection.Field);
                if (mapping == null) continue;
                matched.Add(mapping); projection.Projection["displayName"] = mapping.DisplayName; changed = true;
            }
            if (changed) rows.Add(new(visual.File, before.Bytes(), Bytes(json)));
        }
        if (manifest.Mappings.Count == 0 || matched.Count != manifest.Mappings.Count) throw new InvalidOperationException("Every mapping must match a current field projection and report/page/visual. Remove stale or unrelated rows and preview again.");
        return engine.Prepare(report, "Apply reviewed display names · " + matched.Count + " mappings", rows);
    }
}
