using System.Diagnostics;

namespace PbiBench.DaxStudio;

public sealed record CompanionTool(string Id, string Name, string Ownership, string ExecutableName);
public sealed record CompanionStatus(CompanionTool Tool, string? Path, string? Version)
{ public string Display => Path == null ? "Not installed / configure path" : "Installed · " + (Version ?? "version unavailable"); }
/// <summary>Replaceable external-process adapter. No app DLL or authentication context is loaded.</summary>
public sealed class CompanionTools(IProcessAdapter? process = null)
{
    public static IReadOnlyList<CompanionTool> Catalog { get; } = Array.AsReadOnly(new[]
    {
        new CompanionTool("fabric-toolbox", "Fabric Toolbox", "PbiBench sub-app · .NET 10", "PbiBench.FabricToolbox.exe"),
        new CompanionTool("dataforge", "DataForge", "Companion · versioned data/truth contracts", "DataForge.exe"),
        new CompanionTool("powerbi", "Power BI Desktop", "External · Desktop renderer/author", "PBIDesktop.exe"),
        new CompanionTool("vscode", "VS Code", "External · workspace source editor", "Code.exe"),
        new CompanionTool("codex", "Codex", "Optional external companion", "Codex.exe")
    });
    public CompanionStatus Discover(CompanionTool tool, string? configuredPath, string baseDirectory)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredPath)) candidates.Add(configuredPath!);
        else
        {
            candidates.Add(Path.Combine(baseDirectory, tool.Id, tool.ExecutableName));
            candidates.Add(Path.Combine(baseDirectory, tool.ExecutableName));
            if (tool.Id == "fabric-toolbox")
            {
                var directory = new DirectoryInfo(baseDirectory);
                for (var i = 0; directory != null && i < 7; i++, directory = directory.Parent)
                    if (File.Exists(Path.Combine(directory.FullName, "PbiBench.slnx"))) foreach (var config in new[] { "Debug", "Release" }) candidates.Add(Path.Combine(directory.FullName, "src", "PbiBench.FabricToolbox", "bin", config, "net10.0-windows", tool.ExecutableName));
            }
            if (tool.Id == "powerbi") candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Power BI Desktop", "bin", tool.ExecutableName));
            if (tool.Id == "vscode") candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", tool.ExecutableName));
        }
        var path = candidates.FirstOrDefault(p => Path.IsPathRooted(p) && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p));
        string? version = null;
        if (path != null) { try { version = FileVersionInfo.GetVersionInfo(path).FileVersion; } catch (IOException) { } catch (System.ComponentModel.Win32Exception) { } }
        return new(tool, path, version);
    }
    public void Launch(CompanionStatus status, string? projectDirectory = null)
    {
        if (status.Path == null || !File.Exists(status.Path)) throw new FileNotFoundException(status.Tool.Name + " is not installed. Configure its executable path.");
        var args = status.Tool.Id == "vscode" && projectDirectory != null && Directory.Exists(projectDirectory) ? new[] { Path.GetFullPath(projectDirectory) } : Array.Empty<string>();
        (process ?? new SystemProcessAdapter()).Start(new(status.Path, args));
    }
}
