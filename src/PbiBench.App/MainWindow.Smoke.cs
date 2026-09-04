using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PbiBench.Automation;
using PbiBench.Semantic;

namespace PbiBench.App;

public partial class MainWindow
{
    private bool smokeMode;
    private async Task RunSmokeAsync(string[] args)
    {
        smokeMode = true;
        var index = Array.IndexOf(args, "--smoke-test");
        var outputRoot = index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : Path.Combine(settingsDirectory, "smoke");
        Directory.CreateDirectory(outputRoot);
        var checks = new List<string>();
        try
        {
            var modelFile = Path.Combine(outputRoot, "smoke-model.bim");
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "examples", "pass1-demo.bim"), modelFile, true);
            editor.Open(modelFile);
            await UpdateSessionAsync();
            await Dispatcher.InvokeAsync(() => UpdateLayout(), DispatcherPriority.ContextIdle);
            var h = editor.Handler ?? throw new InvalidOperationException("Editor did not open the BIM fixture.");
            Check(h.Model.Tables.Count == 2 && editor.TreeRootCount > 0, "In-process TE2 model and tree populated", checks);
            var measure = h.Model.Tables["Sales"].Measures["Revenue"];
            editor.Select(measure);
            Check(editor.Selection.Contains(measure), "Tree selection synchronized to real model object", checks);
            Check(editor.ActiveExpression.Contains("SUM"), "TE2 expression editor displays selected measure", checks);
            var original = measure.Description;
            measure.Description = "Smoke verification";
            editor.Undo();
            Check(measure.Description == original, "Property edit and TE2 undo restore original value", checks);
            editor.Redo(); editor.Undo();
            Check(measure.Description == original, "TE2 redo/undo roundtrip", checks);
            GoTo("Model"); await PaintAsync(); Capture(outputRoot, "model");
            Width = 1060; Height = 700; await PaintAsync(); Capture(outputRoot, "model-compact");
            Check(ModelSurface.ActualWidth >= 570 && ModelSurface.ActualHeight >= 300, "Compact viewport retains usable model area", checks);
            Width = 1540; Height = 940; await PaintAsync();
            var preview = automation!.Preview(AutomationActionId.OrganizeMeasures, new[] { measure }, new AutomationOptions { DisplayFolder = "Smoke" });
            Check(preview.Changes.Count == 1 && measure.DisplayFolder != "Smoke", "Automation preview is exact and non-mutating", checks);
            automation.Apply(preview); automation.Undo();
            Check(measure.DisplayFolder != "Smoke", "Automation apply and undo use hosted session", checks);
            GoTo("Automate"); ActionPreviewGrid.ItemsSource = preview.Changes; await PaintAsync(); Capture(outputRoot, "automation");
            GoTo("QA"); ScanBpa(this, new RoutedEventArgs());
            Check(BpaGrid.Items.Count >= 4, "BPA companion reports actionable real-model findings", checks);
            BpaGrid.SelectedIndex = 0; await PaintAsync(); Capture(outputRoot, "bpa");
            GoTo("Model diagram");
            var tableButton = DiagramCanvas.Children.OfType<Button>().First();
            tableButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(editor.Selection.Any(o => ReferenceEquals(o, tableButton.Tag)), "Diagram node click selects model tree object", checks);
            GoTo("Model diagram"); await PaintAsync(); Capture(outputRoot, "diagram");
            var graph = new SemanticModelService(h).GetGraph();
            Check(graph.Relationships.Count == 1 && graph.Relationships[0].FromCardinality == "Many", "Relationship graph has real cardinality", checks);
            editor.Select(measure); GoTo("DAX"); UseExpression(this, new RoutedEventArgs()); FormatScratch(this, new RoutedEventArgs());
            Check(scratch.Text.Contains("EVALUATE") && scratch.Text.Contains("SUM"), "Active expression routes to DAX scratch", checks);
            Check(ToQuery("1 // end").EndsWith("\r\n    )", StringComparison.Ordinal), "Scalar query wrapper preserves trailing comments", checks);
            Check(ToQuery("// comment\nEVALUATE ROW(\"x\", 1)").StartsWith("//", StringComparison.Ordinal), "Query wrapper recognizes leading comments", checks);
            await PaintAsync(); Capture(outputRoot, "dax");
            File.WriteAllText(Path.Combine(outputRoot, "smoke-progress.txt"), "Before local save\n" + string.Join("\n", checks));
            GoTo("Model"); editor.Save();
            Check(File.Exists(modelFile) && !h.HasUnsavedChanges, "Hosted TE2 local save completed", checks);
            File.AppendAllText(Path.Combine(outputRoot, "smoke-progress.txt"), "\nLocal save complete; serializing report");
            File.WriteAllText(Path.Combine(outputRoot, "smoke-result.json"), JsonSerializer.Serialize(new { success = true, checks, screenshots = Directory.GetFiles(outputRoot, "*.png") }, new JsonSerializerOptions { WriteIndented = true }));
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(outputRoot, "smoke-error.txt"), string.Join("\n", checks) + "\n\n" + ex);
            Environment.ExitCode = 1;
        }
        finally { Close(); }
    }
    private static void Check(bool condition, string description, List<string> checks)
    {
        if (!condition) throw new InvalidOperationException(description);
        checks.Add(description);
    }
    private async Task PaintAsync()
    {
        UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }
    private void Capture(string outputRoot, string name)
    {
        var root = (FrameworkElement)Content;
        var width = (int)Math.Ceiling(root.ActualWidth); var height = (int)Math.Ceiling(root.ActualHeight);
        var background = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        background.Render(root);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle((Brush)new BrushConverter().ConvertFromString("#F7F8F6")!, null, new Rect(0, 0, width, height));
            drawing.DrawImage(background, new Rect(0, 0, width, height));
            if (ModelSurface.Visibility == Visibility.Visible || DaxPage.Visibility == Visibility.Visible)
            {
                var surface = ModelSurface.Visibility == Visibility.Visible ? ModelSurface : ScratchSurface;
                using var capture = ModelSurface.Visibility == Visibility.Visible ? editor.Capture() : scratch.Capture();
                var handle = capture.GetHbitmap();
                try
                {
                    var bitmap = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    var point = surface.TranslatePoint(new Point(0, 0), root);
                    drawing.DrawImage(bitmap, new Rect(point, new Size(surface.ActualWidth, surface.ActualHeight)));
                }
                finally { DeleteObject(handle); }
            }
        }
        var complete = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); complete.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(complete));
        using var file = File.Create(Path.Combine(outputRoot, name + ".png")); encoder.Save(file);
    }
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);
}
