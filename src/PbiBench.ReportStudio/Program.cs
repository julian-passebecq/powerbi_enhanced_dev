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
                if (Value("--contract-version") is { } version && version != "1") throw new InvalidDataException("Unsupported Report Studio handoff version.");
                if (Value("--smoke-test") is { } output)
                {
                    await StudioSmoke.RunAsync(window, output); app.Shutdown(0); return;
                }
                var input = Value("--report") ?? args.FirstOrDefault(a => !a.StartsWith("-", StringComparison.Ordinal));
                if (input != null) { await window.OpenAsync(input); window.FocusObject(Value("--page"), Value("--visual")); }
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
