using System.Diagnostics;

namespace PbiBench.ExternalTools;

public sealed record ExternalToolDefinition(string Id, string Name, string Ownership, string ExecutableName);
public sealed record ExternalToolStatus(ExternalToolDefinition Tool, string? Path, string? Version, string? Diagnostic = null)
{ public string Display => Diagnostic ?? (Path == null ? "Not installed / configure path" : "Installed · " + (Version ?? "version unavailable")); }
/// <summary>Replaceable external-process adapter. No app DLL or authentication context is loaded.</summary>
public sealed class CompanionTools(IProcessAdapter? process = null)
{
    private readonly IProcessAdapter adapter = process ?? new SystemProcessAdapter();
    public static IReadOnlyList<ExternalToolDefinition> Catalog { get; } = Array.AsReadOnly(new[]
    {
        new ExternalToolDefinition("fabric-toolbox", "Fabric Toolbox", "PbiBench sub-app · .NET 10", "PbiBench.FabricToolbox.exe"),
        new ExternalToolDefinition("report-studio", "Report Studio", "PbiBench sub-app · local PBIP/PBIR · .NET 10", "PbiBench.ReportStudio.exe"),
        new ExternalToolDefinition("bravo", "Bravo", "External · quick model helper", "Bravo.exe"),
        new ExternalToolDefinition("dataforge", "DataForge", "Companion · versioned data/truth contracts", "DataForge.exe"),
        new ExternalToolDefinition("powerbi", "Power BI Desktop", "External · Desktop renderer/author", "PBIDesktop.exe"),
        new ExternalToolDefinition("vscode", "VS Code", "External · workspace source editor", "Code.exe"),
        new ExternalToolDefinition("codex", "Codex", "Optional external companion", "Codex.exe")
    });
    public ExternalToolStatus Discover(ExternalToolDefinition tool, string? configuredPath, string baseDirectory)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredPath)) candidates.Add(configuredPath!);
        else
        {
            if (tool.Id is "fabric-toolbox" or "report-studio" && ComponentsManifest.Find(baseDirectory) is { } manifest)
            {
                try { candidates.Add(ComponentsManifest.Load(manifest).Resolve(manifest, tool.Id)); }
                catch (Exception error) when (error is InvalidDataException || error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
                { return new(tool, null, null, "Component manifest is invalid or unavailable. Repair the package or configure the module path in Tools."); }
            }
            candidates.Add(Path.Combine(baseDirectory, tool.Id, tool.ExecutableName));
            candidates.Add(Path.Combine(baseDirectory, tool.ExecutableName));
            if (tool.Id is "fabric-toolbox" or "report-studio")
            {
                var directory = new DirectoryInfo(baseDirectory);
                for (var i = 0; directory != null && i < 7; i++, directory = directory.Parent)
                    if (File.Exists(Path.Combine(directory.FullName, "PbiBench.slnx"))) foreach (var config in new[] { baseDirectory.IndexOf("Release", StringComparison.OrdinalIgnoreCase) >= 0 ? "Release" : "Debug", baseDirectory.IndexOf("Release", StringComparison.OrdinalIgnoreCase) >= 0 ? "Debug" : "Release" }) candidates.Add(Path.Combine(directory.FullName, "src", tool.Id == "fabric-toolbox" ? "PbiBench.FabricToolbox" : "PbiBench.ReportStudio", "bin", config, "net10.0-windows", tool.ExecutableName));
            }
            if (tool.Id == "powerbi") candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Power BI Desktop", "bin", tool.ExecutableName));
            if (tool.Id == "vscode") candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", tool.ExecutableName));
            if (tool.Id == "bravo") foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
                foreach (var folder in new[] { "Bravo", "SQLBI\\Bravo", "Programs\\Bravo" }) candidates.Add(Path.Combine(root, folder, tool.ExecutableName));
            if (tool.Id == "vscode") candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", tool.ExecutableName));
        }
        var path = candidates.Where(p => Path.IsPathRooted(Environment.ExpandEnvironmentVariables(p.Trim().Trim('"')))).Select(ExecutableDiscovery.ExistingExecutable).FirstOrDefault(p => p != null);
        string? version = null;
        if (path != null) { try { version = FileVersionInfo.GetVersionInfo(tool.Id is "fabric-toolbox" or "report-studio" && File.Exists(Path.ChangeExtension(path, ".dll")) ? Path.ChangeExtension(path, ".dll") : path).FileVersion; } catch (IOException) { } catch (System.ComponentModel.Win32Exception) { } }
        return new(tool, path, version);
    }
    public void Launch(ExternalToolStatus status, string? projectDirectory = null)
    {
        Launch(status, new ToolContext(ProjectDirectory: projectDirectory));
    }
    public void Launch(ExternalToolStatus status, ToolContext context)
    {
        if (status.Path == null || !File.Exists(status.Path)) throw new FileNotFoundException(status.Tool.Name + " is missing; configure its executable path.");
        var applicability = ExternalToolContext.Evaluate(status, context);
        if (!applicability.Enabled) throw new InvalidOperationException(applicability.Reason);
        var request = new ProcessLaunchRequest(status.Path!, applicability.Arguments);
        if (status.Tool.Id is "report-studio" or "fabric-toolbox" && adapter is IFocusProcessAdapter focus) focus.StartOrFocus(request);
        else adapter.Start(request);
    }
}
