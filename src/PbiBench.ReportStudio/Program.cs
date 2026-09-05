using System.IO;
using System.Windows;

namespace PbiBench.ReportStudio;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application(); var window = new StudioWindow();
        window.Loaded += async (_, _) =>
        {
            string? Value(string key) { var i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            try
            {
                if (Value("--smoke-test") is { } output)
                {
                    await StudioSmoke.RunAsync(window, output); app.Shutdown(0); return;
                }
                var handoff = PbiBench.ExternalTools.ModuleHandoff.Parse(args, reportModule: true);
                if (handoff.Report != null) { await window.OpenAsync(handoff.Report); window.FocusObject(handoff.Page, handoff.Visual); }
                if (handoff.ProjectContext != null) await window.AcceptProjectContextAsync(handoff.ProjectContext);
                if (handoff.ModelContext != null) await window.OpenDesignAsync(handoff.ModelContext, handoff.DashboardSpec, handoff.Theme);

            }
            catch (Exception error)
            {
                if (Value("--smoke-test") is { } path) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); await File.WriteAllTextAsync(path, error.ToString()); app.Shutdown(1); }
                else window.ShowError(error);
            }
        };
        return app.Run(window);
    }
}
