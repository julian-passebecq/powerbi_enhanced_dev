namespace PbiBench.Dax.LanguageService;

public sealed record DaxNavigationPoint(string DocumentId, int Offset, string? SymbolId = null);
public sealed class DaxNavigationHistory
{
    private readonly List<DaxNavigationPoint> points = new();
    private readonly int capacity;
    private int index = -1;
    public DaxNavigationHistory(int capacity = 100) => this.capacity = Math.Max(2, capacity);
    public bool CanGoBack => index > 0;
    public bool CanGoForward => index >= 0 && index < points.Count - 1;
    public DaxNavigationPoint? Current => index >= 0 ? points[index] : null;
    public void Visit(DaxNavigationPoint point)
    {
        if (point == Current) return;
        if (index < points.Count - 1) points.RemoveRange(index + 1, points.Count - index - 1);
        points.Add(point);
        if (points.Count > capacity) points.RemoveAt(0);
        index = points.Count - 1;
    }
    public DaxNavigationPoint? Back() => CanGoBack ? points[--index] : null;
    public DaxNavigationPoint? Forward() => CanGoForward ? points[++index] : null;
}
