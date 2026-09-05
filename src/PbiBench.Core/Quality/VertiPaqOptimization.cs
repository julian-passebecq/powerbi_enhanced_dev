namespace PbiBench.Core.Quality;

/// <summary>Original evidence-driven suggestions. No rule here supplies an automatic metadata mutation.</summary>
public static class VertiPaqOptimization
{
    public static IReadOnlyList<OptimizationSignal> Build(VertiPaqSnapshot? snapshot, IEnumerable<OptimizationSignal>? otherSignals = null)
    {
        var signals = (otherSignals ?? Array.Empty<OptimizationSignal>()).ToList();
        if (snapshot == null) return signals;
        foreach (var column in snapshot.Columns.Where(column => column.TotalBytes > 0).OrderByDescending(column => column.TotalBytes).Take(10))
            signals.Add(new("VPAX_SIZE:" + column.Table + ":" + column.Name, "VertiPaq " + snapshot.Source, "Size", "BENCHMARK", "Review a large column",
                $"{column.TotalBytes:N0} bytes: {column.DataBytes:N0} data, {column.DictionaryBytes:N0} dictionary, {column.HierarchyBytes:N0} hierarchy. Captured {snapshot.CapturedAt:O}.",
                column.Table, column.Name, "Profile its values and report requirements; compare a reviewed proposal against representative queries. Size alone does not establish redundancy."));
        foreach (var table in snapshot.Tables.Where(table => table.StorageMode == "DirectLake" || table.StorageMode == "Mixed"))
            signals.Add(new("VPAX_RESIDENCY:" + table.Name, "VertiPaq", "Refresh", "REVIEW", "Review resident-data coverage",
                table.StorageMode + " metrics reflect captured residency and extraction scope.", table.Name, null, "Inspect segment residency and supported Direct Lake workflows before comparing memory captures."));
        foreach (var relationship in snapshot.Relationships.Where(relationship => relationship.InvalidRows > 0 || relationship.MissingKeys > 0))
            signals.Add(new("VPAX_RI:" + relationship.Name, "VertiPaq", "Correctness", "REVIEW", "Relationship keys require review",
                $"{relationship.MissingKeys:N0} missing keys; {relationship.InvalidRows:N0} invalid rows in the captured statistics.", relationship.FromTable, relationship.FromColumn,
                "Run current relationship coverage under the intended identity. Correct the data or model through an explicit reviewed plan."));
        return signals.ToArray();
    }
}
