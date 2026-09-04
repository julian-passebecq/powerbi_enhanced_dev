using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace PbiBench.App;

/// <summary>Shell layout in device-independent pixels. Native TE2 panes retain their own preferences.</summary>
[DataContract]
public sealed class AppLayoutState
{
    [DataMember] public int Version { get; set; } = 1;
    [DataMember] public double Width { get; set; } = 1540;
    [DataMember] public double Height { get; set; } = 940;
    [DataMember] public double? Left { get; set; }
    [DataMember] public double? Top { get; set; }
    [DataMember] public bool Maximized { get; set; }
    [DataMember] public double InspectorWidth { get; set; } = 280;
    [DataMember] public double OutputHeight { get; set; } = 160;
    [DataMember] public bool InspectorVisible { get; set; } = true;
    [DataMember] public bool OutputVisible { get; set; }
    [DataMember] public string SelectedPage { get; set; } = "Home";
    [DataMember] public List<string> RecentProjects { get; set; } = new();
    [DataMember] public Dictionary<string, double> NativePaneFractions { get; set; } = new();

    // DataContract deserialization skips property initializers when fields are absent.
    [OnDeserializing]
    private void SetDefaults(StreamingContext _)
    {
        Version = 1; Width = 1540; Height = 940; InspectorWidth = 280; OutputHeight = 160;
        InspectorVisible = true; SelectedPage = "Home"; RecentProjects = new List<string>();
    }

    public void RememberProject(string path)
    {
        var normalized = NormalizePath(path);
        if (normalized == null) return;
        RecentProjects ??= new List<string>();
        RecentProjects.RemoveAll(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
        RecentProjects.Insert(0, normalized);
        if (RecentProjects.Count > 12) RecentProjects.RemoveRange(12, RecentProjects.Count - 12);
    }

    internal AppLayoutState Normalize()
    {
        var recents = (RecentProjects ?? new List<string>()).Select(NormalizePath)
            .Where(path => path != null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
        return new AppLayoutState
        {
            Width = Limit(Width, 1050, 10000, 1540), Height = Limit(Height, 650, 8000, 940),
            Left = Coordinate(Left), Top = Coordinate(Top), Maximized = Maximized,
            InspectorWidth = Limit(InspectorWidth, 210, 1200, 280),
            OutputHeight = Limit(OutputHeight, 80, 1200, 160),
            InspectorVisible = InspectorVisible, OutputVisible = OutputVisible,
            SelectedPage = Pages.Contains(SelectedPage ?? "") ? SelectedPage! : "Home", RecentProjects = recents,
            NativePaneFractions = (NativePaneFractions ?? new Dictionary<string, double>()).Where(p => p.Key == "splitContainer1" || p.Key == "splitContainer2").ToDictionary(p => p.Key, p => Limit(p.Value, .1, .9, .5))
        };
    }

    private static readonly HashSet<string> Pages = new(StringComparer.Ordinal)
        { "Home", "Model", "DAX", "Automate", "Model diagram", "Diagram", "PBIP / Git", "QA", "Report", "Fabric", "Deploy", "Knowledge", "Agent" };

    private static double Limit(double value, double minimum, double maximum, double fallback) =>
        double.IsNaN(value) || double.IsInfinity(value) ? fallback : Math.Max(minimum, Math.Min(maximum, value));

    private static double? Coordinate(double? value) => value.HasValue && !double.IsNaN(value.Value) &&
        !double.IsInfinity(value.Value) && Math.Abs(value.Value) <= 100000 ? value : null;

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path!.Length > 32767) return null;
        try { return Path.IsPathRooted(path) ? Path.GetFullPath(path) : null; }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
        catch (PathTooLongException) { return null; }
    }
}

/// <summary>Small, isolated preference file; malformed settings never prevent application launch.</summary>
public sealed class LayoutStateStore
{
    private readonly string filePath;
    public LayoutStateStore(string settingsDirectory) => filePath = Path.Combine(Path.GetFullPath(settingsDirectory), "layout-v7.json");
    public string FilePath => filePath;

    public AppLayoutState Load()
    {
        try
        {
            if (!File.Exists(filePath) || new FileInfo(filePath).Length > 262144) return new AppLayoutState();
            using var stream = File.OpenRead(filePath);
            var state = (AppLayoutState?)new DataContractJsonSerializer(typeof(AppLayoutState)).ReadObject(stream);
            return state?.Version == 1 ? state.Normalize() : new AppLayoutState();
        }
        catch (Exception error) when (IsRecoverable(error)) { return new AppLayoutState(); }
    }

    public bool TrySave(AppLayoutState state, out string? error)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                new DataContractJsonSerializer(typeof(AppLayoutState)).WriteObject(stream, state.Normalize());
                stream.Flush(true);
            }
            if (File.Exists(filePath)) File.Replace(temporary, filePath, null);
            else File.Move(temporary, filePath);
            error = null;
            return true;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            error = "Layout preferences could not be saved: " + exception.Message;
            return false;
        }
        finally
        {
            if (temporary != null)
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception exception) when (IsRecoverable(exception)) { }
        }
    }

    private static bool IsRecoverable(Exception error) => error is IOException || error is UnauthorizedAccessException ||
        error is System.Security.SecurityException || error is SerializationException || error is System.Xml.XmlException || error is ArgumentException;
}
