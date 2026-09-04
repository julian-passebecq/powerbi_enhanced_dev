using PbiBench.Core.Queries;

namespace PbiBench.Core.DataExploration;

public sealed record DataProfileOptions(int TopCount = 20, bool IncludeAdvanced = false)
{
    internal void Validate()
    {
        if (TopCount < 1 || TopCount > 200) throw new ArgumentOutOfRangeException(nameof(TopCount), "Profile samples must contain between 1 and 200 values.");
    }
}

/// <summary>A reviewable, read-only query. Constructing it never connects to or scans a model.</summary>
public sealed record DataProfilePlan(string Title, string Query, IReadOnlyList<string> ResultNames,
    IReadOnlyList<string> Warnings, bool IsExpensive)
{
    public QueryResult LabelResults(QueryResult result) => result with
    {
        Results = result.Results.Select((set, index) => index < ResultNames.Count ? set with { Name = ResultNames[index] } : set).ToArray()
    };
}
