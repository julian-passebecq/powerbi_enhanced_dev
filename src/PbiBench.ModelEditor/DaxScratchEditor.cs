using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using FastColoredTextBoxNS;

namespace PbiBench.ModelEditor;
public sealed class DaxScratchEditor : IDisposable
{
    private readonly FastColoredTextBox editor;
    private readonly TextStyle keyword = new(Brushes.MediumBlue, null, FontStyle.Bold);
    private readonly TextStyle literal = new(Brushes.DarkRed, null, FontStyle.Regular);
    private readonly TextStyle comment = new(Brushes.SeaGreen, null, FontStyle.Italic);
    public WindowsFormsHost View { get; }
    public string Text { get => editor.Text; set => editor.Text = value; }
    public Bitmap Capture()
    {
        var bitmap = new Bitmap(Math.Max(1, editor.Width), Math.Max(1, editor.Height));
        editor.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        return bitmap;
    }
    public DaxScratchEditor()
    {
        editor = new FastColoredTextBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 11), ShowLineNumbers = true, BackColor = Color.White, AutoIndent = true };
        editor.TextChanged += (_, e) =>
        {
            editor.Range.ClearStyle(keyword, literal, comment);
            editor.Range.SetStyle(keyword, @"\b(VAR|RETURN|EVALUATE|DEFINE|MEASURE|ORDER\s+BY|ASC|DESC|SUM|SUMX|CALCULATE|FILTER|DIVIDE|COUNTROWS|VALUES|ALL|IF|BLANK|ROW)\b", RegexOptions.IgnoreCase);
            editor.Range.SetStyle(literal, "\"(?:\"\"|[^\"])*\"|'(?:''|[^'])*'");
            editor.Range.SetStyle(comment, @"//[^\r\n]*|--[^\r\n]*|/\*[\s\S]*?\*/");
        };
        View = new WindowsFormsHost { Child = editor };
    }
    public void Dispose() { View.Dispose(); keyword.Dispose(); literal.Dispose(); comment.Dispose(); }
}
