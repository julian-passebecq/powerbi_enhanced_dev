using System.IO;
using System.Windows;

namespace PbiBench.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try { MainWindow = new MainWindow(); MainWindow.Show(); }
        catch (Exception ex)
        {
            var index = Array.IndexOf(e.Args, "--smoke-test");
            if (index >= 0 && index + 1 < e.Args.Length)
            {
                Directory.CreateDirectory(e.Args[index + 1]);
                File.WriteAllText(Path.Combine(e.Args[index + 1], "startup-error.txt"), ex.ToString());
            }
            else MessageBox.Show("PbiBench could not start. Run scripts/build-pass1.ps1 to restore the pinned TE2 runtime.\n\n" + ex.Message, "PbiBench", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
