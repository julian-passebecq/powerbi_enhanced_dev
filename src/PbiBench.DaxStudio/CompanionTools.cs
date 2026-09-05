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
        new CompanionTool("report-studio", "Report Studio", "PbiBench sub-app · local PBIP/PBIR · .NET 10", "PbiBench.ReportStudio.exe"),
        new CompanionTool("bravo", "Bravo", "External · quick model helper", "Bravo.exe"),
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
        var path = candidates.FirstOrDefault(p => Path.IsPathRooted(p) && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p));
        string? version = null;
        if (path != null) { try { version = FileVersionInfo.GetVersionInfo(path).FileVersion; } catch (IOException) { } catch (System.ComponentModel.Win32Exception) { } }
        return new(tool, path, version);
    }
    public void Launch(CompanionStatus status, string? projectDirectory = null)
    {
        Launch(status, new ToolContext(ProjectDirectory: projectDirectory));
    }
    public void Launch(CompanionStatus status, ToolContext context)
    {
        if (status.Path == null || !File.Exists(status.Path)) throw new FileNotFoundException(status.Tool.Name + " is missing; configure its executable path.");
        var applicability = ExternalToolContext.Evaluate(status, context);
        if (!applicability.Enabled) throw new InvalidOperationException(applicability.Reason);
        (process ?? new SystemProcessAdapter()).Start(new(status.Path!, applicability.Arguments));
    }
}
