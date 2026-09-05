using System.Globalization;

namespace PbiBench.Core.Fabric;

public sealed record FabricJobInstance(string Id, string WorkspaceId, string ItemId, string ItemName, string ItemKind,
    string JobType, string Status, string InvokeType, DateTimeOffset? StartTimeUtc, DateTimeOffset? EndTimeUtc,
    string? RootActivityId, string? FailureSummary)
{
    // Running durations use the captured response time, so filtering never changes the result.
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    public TimeSpan? Duration => StartTimeUtc is { } start && (EndTimeUtc ?? CapturedAt) >= start ? (EndTimeUtc ?? CapturedAt) - start : null;
    public string Detail => ItemName + " · " + ItemKind + "\nWorkspace: " + WorkspaceId + "\nItem: " + ItemId + "\nJob instance: " + Id +
        "\nCorrelation: " + (RootActivityId ?? "Not supplied") + "\n" + JobType + " · " + Status + " · " + InvokeType +
        "\nStart (UTC): " + (StartTimeUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "Not supplied") +
        "\nEnd (UTC): " + (EndTimeUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "Not supplied") +
        "\nDuration" + (EndTimeUtc == null ? " at capture" : "") + ": " + (Duration?.ToString() ?? "Not supplied") + "\n" + (FailureSummary ?? "No failure details supplied.");
}
public sealed record FabricJobQuery(int MaximumPages = 10, int MaximumItems = 1000)
{
    public void Validate() { if (MaximumPages is < 1 or > 20 || MaximumItems is < 1 or > 5000) throw new ArgumentException("Use 1–20 pages and 1–5,000 job instances."); }
}
public sealed record FabricJobInventory(FabricItem Item, IReadOnlyList<FabricJobInstance> Jobs, bool Supported, bool Truncated, string Notice);
public interface IFabricOperationsService
{
    Task<FabricJobInventory> ListRecentAsync(FabricItem item, FabricJobQuery query, CancellationToken cancellationToken);
}
public static class FabricJobSupport
{
    public static bool Supports(string kind) => kind is "Notebook" or "DataPipeline" or "SparkJobDefinition";
    public static string Describe(string kind) => Supports(kind) ? "Recent jobs available. Refresh manually in Operations." : "Recent jobs for " + kind + " are not supported by Toolbox V0.2.";
}
