using PbiBench.App;
using Xunit;

namespace PbiBench.Adapters.Tests;

public sealed class LayoutStateTests
{
    [Fact]
    public void ReopenRetainsWindowAndPaneStateAndRecentProjects()
    {
        using var temp = new TemporaryDirectory();
        var store = new LayoutStateStore(Path.Combine(temp.Root, "profile"));
        var state = new AppLayoutState
        {
            Width = 1280, Height = 800, Left = -1100, Top = 50, Maximized = true,
            InspectorWidth = 330, OutputHeight = 240, InspectorVisible = false,
            OutputVisible = true, SelectedPage = "DAX"
        };
        var model = temp.Write("working/Sales.bim", "{}");
        state.RememberProject(model);
        Assert.True(store.TrySave(state, out var error), error);
        var loaded = new LayoutStateStore(Path.Combine(temp.Root, "profile")).Load();
        Assert.Equal(1280, loaded.Width); Assert.Equal(800, loaded.Height);
        Assert.Equal(-1100, loaded.Left); Assert.Equal(50, loaded.Top); Assert.True(loaded.Maximized);
        Assert.Equal(330, loaded.InspectorWidth); Assert.Equal(240, loaded.OutputHeight);
        Assert.False(loaded.InspectorVisible); Assert.True(loaded.OutputVisible);
        Assert.Equal("DAX", loaded.SelectedPage); Assert.Equal(model, Assert.Single(loaded.RecentProjects));
        loaded.OutputHeight = 200;
        Assert.True(store.TrySave(loaded, out error), error);
        Assert.Equal(200, store.Load().OutputHeight);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(store.FilePath)!, "*.tmp"));
    }

    [Fact]
    public void InvalidAndFuturePreferencesCannotPreventLaunchOrCreateUnusablePanes()
    {
        using var temp = new TemporaryDirectory();
        var store = new LayoutStateStore(temp.Root);
        File.WriteAllText(store.FilePath, "this is not JSON");
        Assert.Equal(1540, store.Load().Width);
        File.WriteAllText(store.FilePath, "{\"Version\":99,\"Width\":2000}");
        Assert.Equal(1540, store.Load().Width);
        File.WriteAllText(store.FilePath, "{\"Width\":-1,\"Height\":900000,\"InspectorWidth\":0,\"OutputHeight\":9000,\"SelectedPage\":\"invalid\",\"RecentProjects\":null}");
        var loaded = store.Load();
        Assert.Equal(1050, loaded.Width); Assert.Equal(8000, loaded.Height);
        Assert.Equal(210, loaded.InspectorWidth); Assert.Equal(1200, loaded.OutputHeight);
        Assert.Equal("Home", loaded.SelectedPage); Assert.Empty(loaded.RecentProjects);
        Assert.True(loaded.InspectorVisible);
        File.WriteAllText(store.FilePath, new string(' ', 262145));
        Assert.Equal(1540, store.Load().Width);
    }

    [Fact]
    public void RecentProjectsAreBoundedUniqueAbsolutePathsAndRetainMostRecentOrder()
    {
        using var temp = new TemporaryDirectory();
        var state = new AppLayoutState();
        for (var index = 0; index < 20; index++) state.RememberProject(Path.Combine(temp.Root, $"model-{index}.bim"));
        Assert.Equal(12, state.RecentProjects.Count);
        var first = state.RecentProjects[3];
        state.RememberProject(first.ToUpperInvariant());
        Assert.Equal(12, state.RecentProjects.Count);
        Assert.Equal(first.ToUpperInvariant(), state.RecentProjects[0]);
        state.RememberProject("relative.bim"); state.RememberProject("\0");
        Assert.Equal(12, state.RecentProjects.Count);
        Assert.All(state.RecentProjects, path => Assert.True(Path.IsPathRooted(path)));
    }

    [Fact]
    public void SmokeAndUserProfilesCannotOverwriteEachOther()
    {
        using var temp = new TemporaryDirectory();
        var user = new LayoutStateStore(Path.Combine(temp.Root, "user"));
        var smoke = new LayoutStateStore(Path.Combine(temp.Root, "smoke"));
        Assert.True(user.TrySave(new AppLayoutState { InspectorWidth = 360 }, out _));
        Assert.True(smoke.TrySave(new AppLayoutState { InspectorWidth = 225 }, out _));
        Assert.Equal(360, user.Load().InspectorWidth);
        Assert.Equal(225, smoke.Load().InspectorWidth);
    }

    [Fact]
    public void LockedPreferencesRemainIntactWhenSavingFails()
    {
        using var temp = new TemporaryDirectory();
        var store = new LayoutStateStore(temp.Root);
        Assert.True(store.TrySave(new AppLayoutState { InspectorWidth = 360 }, out _));
        using (File.Open(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False(store.TrySave(new AppLayoutState { InspectorWidth = 240 }, out var error));
            Assert.Contains("could not be saved", error);
        }
        Assert.Equal(360, store.Load().InspectorWidth);
        Assert.Empty(Directory.GetFiles(temp.Root, "*.tmp"));
    }

    [Fact]
    public void NonFiniteGeometryIsNormalizedBeforeJsonSerialization()
    {
        using var temp = new TemporaryDirectory();
        var store = new LayoutStateStore(temp.Root);
        Assert.True(store.TrySave(new AppLayoutState
        {
            Width = double.NaN, Height = double.PositiveInfinity, Top = double.NegativeInfinity,
            Left = 100001, InspectorWidth = double.NaN, OutputHeight = double.PositiveInfinity
        }, out var error), error);
        var loaded = store.Load();
        Assert.Equal(1540, loaded.Width); Assert.Equal(940, loaded.Height);
        Assert.Null(loaded.Top); Assert.Null(loaded.Left);
        Assert.Equal(280, loaded.InspectorWidth); Assert.Equal(160, loaded.OutputHeight);
    }

    [Fact]
    public void OriginalIconContainsSevenValidFullColorPngResolutions()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "PbiBench.ico");
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        Assert.Equal(0, reader.ReadUInt16()); Assert.Equal(1, reader.ReadUInt16()); Assert.Equal(7, reader.ReadUInt16());
        var entries = new List<(int Size, uint Length, uint Offset)>();
        foreach (var size in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            Assert.Equal(size == 256 ? 0 : size, reader.ReadByte());
            Assert.Equal(size == 256 ? 0 : size, reader.ReadByte());
            Assert.Equal(0, reader.ReadByte()); Assert.Equal(0, reader.ReadByte());
            Assert.Equal(1, reader.ReadUInt16()); Assert.Equal(32, reader.ReadUInt16());
            entries.Add((size, reader.ReadUInt32(), reader.ReadUInt32()));
        }
        foreach (var entry in entries)
        {
            Assert.InRange(entry.Offset + entry.Length, 0u, (uint)stream.Length);
            stream.Position = entry.Offset;
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, reader.ReadBytes(8));
            stream.Position = entry.Offset + 16;
            Assert.Equal(entry.Size, BigEndianInt(reader)); Assert.Equal(entry.Size, BigEndianInt(reader));
        }
    }

    private static int BigEndianInt(BinaryReader reader) => reader.ReadByte() << 24 | reader.ReadByte() << 16 | reader.ReadByte() << 8 | reader.ReadByte();
}
