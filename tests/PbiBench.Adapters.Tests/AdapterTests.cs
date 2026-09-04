using System.Diagnostics;
using System.Runtime.InteropServices;
using PbiBench.DaxStudio;
using PbiBench.Git;
using PbiBench.Workspace;
using Xunit;

namespace PbiBench.Adapters.Tests;

public sealed class DaxStudioTests
{
    [Fact]
    public async Task OpensCurrentQueryWithExactConnectionAndFile()
    {
        using var temp = new TemporaryDirectory();
        var exe = temp.Write("Configured install/DaxStudio.exe", "");
        var process = new FakeProcessAdapter();
        var bridge = new DaxStudioBridge(exe, processAdapter: process);
        const string query = "EVALUATE ROW ( \"Größe\", 42 )\n";
        var scratch = await bridge.OpenQueryAsync(query, "localhost:52122", "Sales & Marketing \"EU\"", temp.Root);
        Assert.Equal(query, File.ReadAllText(scratch));
        Assert.Equal(".dax", Path.GetExtension(scratch));
        Assert.NotNull(process.Started);
        Assert.Equal(exe, process.Started.Executable);
        Assert.Equal(new[] { "--server", "localhost:52122", "--database", "Sales & Marketing \"EU\"", "--file", scratch }, process.Started.Arguments);
        Assert.Equal(0, process.ExecutionCount);
    }

    [Fact]
    public async Task OfflineModelOpensOnlyQueryFile()
    {
        using var temp = new TemporaryDirectory();
        var process = new FakeProcessAdapter();
        var bridge = new DaxStudioBridge(temp.Write("DaxStudio.exe", ""), processAdapter: process);
        var file = await bridge.OpenQueryAsync("SUM ( Sales[Amount] )", database: "Offline metadata name", scratchDirectory: temp.Root);
        Assert.NotNull(process.Started);
        Assert.Equal(new[] { "--file", file }, process.Started.Arguments);
    }

    [Fact]
    public async Task ScratchFilesNeverOverwritePriorQueries()
    {
        using var temp = new TemporaryDirectory();
        var bridge = new DaxStudioBridge(temp.Write("DaxStudio.exe", ""), processAdapter: new FakeProcessAdapter());
        var first = await bridge.OpenQueryAsync("EVALUATE ROW ( \"A\", 1 )", scratchDirectory: temp.Root);
        var second = await bridge.OpenQueryAsync("EVALUATE ROW ( \"A\", 2 )", scratchDirectory: temp.Root);
        Assert.NotEqual(first, second);
        Assert.Contains("1", File.ReadAllText(first));
    }

    [Fact]
    public async Task MissingInstallationIsActionableAndLeavesNoScratchFiles()
    {
        using var temp = new TemporaryDirectory();
        var bridge = new DaxStudioBridge(Path.Combine(temp.Root, "missing.exe"));
        var error = await Assert.ThrowsAsync<FileNotFoundException>(() => bridge.OpenQueryAsync("EVALUATE Sales", scratchDirectory: temp.Root));
        Assert.Contains("configure", error.Message);
        Assert.Empty(Directory.GetFiles(temp.Root));
    }

    [Fact]
    public async Task CancellationDoesNotCreateFileOrLaunchProcess()
    {
        using var temp = new TemporaryDirectory();
        var process = new FakeProcessAdapter();
        var bridge = new DaxStudioBridge(temp.Write("DaxStudio.exe", ""), processAdapter: process);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bridge.OpenQueryAsync("EVALUATE Sales", scratchDirectory: temp.Root, ct: cancellation.Token));
        Assert.Null(process.Started);
        Assert.Empty(Directory.GetFiles(temp.Root, "*.dax"));
    }

    [Fact]
    public void DiscoveryHonorsExplicitPathAndDoesNotFallBackForInvalidConfiguration()
    {
        using var temp = new TemporaryDirectory();
        var executable = temp.Write("Portable/DAXSTUDIO.EXE", "");
        Assert.Equal(executable, DaxStudioLocator.Discover(executable));
        Assert.Null(DaxStudioLocator.Discover(Path.Combine(temp.Root, "does-not-exist.exe")));
        Assert.Null(DaxStudioLocator.Discover(temp.Write("script.cmd", "")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost:54321")]
    [InlineData("powerbi://api.powerbi.com/v1.0/myorg/Sales EU")]
    [InlineData("C:\\Working Folder\\")]
    [InlineData("quote\"in the middle")]
    [InlineData("slashes\\\\\"quoted\\")]
    [InlineData("Größe 日本語 & %PATH% $(echo bad) `test`")]
    [InlineData("line\nbreak\tand space")]
    public void WindowsArgumentsRoundTripThroughOperatingSystemParser(string argument)
    {
        var parsed = CommandLineToArgvW("program " + WindowsCommandLine.Quote(argument), out var count);
        Assert.NotEqual(IntPtr.Zero, parsed);
        try
        {
            Assert.Equal(2, count);
            Assert.Equal(argument, Marshal.PtrToStringUni(Marshal.ReadIntPtr(parsed, IntPtr.Size)));
        }
        finally { LocalFree(parsed); }
    }

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private sealed class FakeProcessAdapter : IProcessAdapter
    {
        public ProcessLaunchRequest? Started { get; private set; }
        public int ExecutionCount { get; private set; }
        public void Start(ProcessLaunchRequest request) => Started = request;
        public Task<ProcessResult> RunAsync(ProcessLaunchRequest request, CancellationToken ct = default)
        { ExecutionCount++; return Task.FromResult(new ProcessResult(0, "", "")); }
    }
}

public sealed class WorkspaceTests
{
    [Fact]
    public async Task DetectsAncestorPbipAndSemanticReportArtifacts()
    {
        using var temp = new TemporaryDirectory();
        temp.Write("Sales.pbip", "{}");
        var modelFile = temp.Write("Sales.SemanticModel/definition/tables/Sales.tmdl", "table Sales");
        temp.Write("Sales.Report/definition.pbir", "{}");
        temp.Write("Sales.Report/definition/report.json", "{}");
        temp.Write("Sales.SemanticModel/DAXQueries/Query.dax", "EVALUATE Sales");
        temp.Write("Sales.SemanticModel/.pbi/unappliedChanges.json", "{}");
        var inventory = await new PbipWorkspaceScanner().DetectAsync(modelFile);
        Assert.NotNull(inventory);
        Assert.Equal(temp.Root, inventory.Root);
        Assert.True(inventory.HasTmdl);
        Assert.True(inventory.HasPbir);
        Assert.True(inventory.HasEnhancedPbir);
        Assert.Single(inventory.DaxQueryFiles);
        Assert.Equal(new[] { Path.Combine(temp.Root, "Sales.SemanticModel") }, inventory.SemanticModelFolders);
        Assert.Contains(inventory.Warnings, warning => warning.Contains("unappliedChanges.json"));
    }

    [Fact]
    public void ScanExcludesGitAndCachesFromArtifactInventory()
    {
        using var temp = new TemporaryDirectory();
        temp.Write("Model.pbip", "{}");
        temp.Write(".git/objects/false.tmdl", "ignored");
        temp.Write("node_modules/false.pbip", "ignored");
        temp.Write("Model.SemanticModel/.pbi/cache.tmdl", "ignored");
        var inventory = new PbipWorkspaceScanner().Scan(temp.Root);
        Assert.Single(inventory.PbipFiles);
        Assert.Empty(inventory.TmdlFiles);
    }

    [Fact]
    public void NearestPbipWinsAndNonProjectReturnsNull()
    {
        using var temp = new TemporaryDirectory();
        Assert.Null(new PbipWorkspaceScanner().Detect(temp.Root));
        temp.Write("Outer.pbip", "{}");
        temp.Write("Nested/Inner.pbip", "{}");
        var model = temp.Write("Nested/Inner.SemanticModel/model.bim", "{}");
        Assert.Equal(Path.Combine(temp.Root, "Nested"), new PbipWorkspaceScanner().Detect(model)!.Root);
    }

    [Fact]
    public void ScanReportsLongPathsAndHonorsCancellation()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.Root, new string('x', 100));
        Directory.CreateDirectory(root);
        var inventory = new PbipWorkspaceScanner().Scan(root);
        Assert.Contains(inventory.Warnings, warning => warning.Contains("paths are long"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new PbipWorkspaceScanner().Scan(root, cancellation.Token));
    }
}

public sealed class GitTests
{
    [Fact]
    public void PorcelainPreservesUnicodeSpacesAndRenameOrder()
    {
        using var temp = new TemporaryDirectory();
        var data = "## feature/model...origin/feature/model [ahead 2]\0"
            + "R  Sales.SemanticModel/definition/tables/Größe new.tmdl\0Sales.SemanticModel/definition/tables/Old.tmdl\0"
            + " M Sales.Report/definition/report.json\0?? queries/new file.dax\0";
        var status = GitClient.ParseStatus(temp.Root, data);
        Assert.Equal("feature/model", status.Branch);
        Assert.True(status.IsDirty);
        Assert.Equal(3, status.Changes.Count);
        Assert.Equal("Sales.SemanticModel/definition/tables/Old.tmdl", status.Changes[0].OriginalPath);
        Assert.Equal(new[] { "Sales.SemanticModel/definition/tables/Größe new.tmdl" }, status.ChangedSemanticFiles);
    }

    [Fact]
    public void RenameOutOfSemanticFolderStillCountsAsSemanticChange()
    {
        using var temp = new TemporaryDirectory();
        var status = GitClient.ParseStatus(temp.Root, "## main\0R  archived/file.json\0semantic/custom.json\0",
            new[] { Path.Combine(temp.Root, "semantic") });
        Assert.Single(status.ChangedSemanticFiles);
        Assert.True(status.Changes[0].IsSemantic);
    }

    [Fact]
    public void ConflictsProduceVisibleWarningAndCleanBranchIsClean()
    {
        using var temp = new TemporaryDirectory();
        var dirty = GitClient.ParseStatus(temp.Root, "## main\0UU Model/model.bim\0");
        Assert.Contains(dirty.Warnings, warning => warning.Contains("conflicts"));
        var clean = GitClient.ParseStatus(temp.Root, "## main...origin/main\0");
        Assert.False(clean.IsDirty);
        Assert.Equal("main · clean", clean.Summary);
    }

    [Theory]
    [InlineData("No commits yet on main", "main")]
    [InlineData("No commits yet on main...origin/main [gone]", "main")]
    [InlineData("Initial commit on develop", "develop")]
    [InlineData("Initial commit on develop...origin/develop [gone]", "develop")]
    [InlineData("HEAD (no branch)", null)]
    public void ParsesUnbornAndDetachedBranches(string header, string? expected)
    {
        using var temp = new TemporaryDirectory();
        Assert.Equal(expected, GitClient.ParseStatus(temp.Root, "## " + header + "\0").Branch);
    }

    [Fact]
    public async Task UsesRepositoryRootForStatusAndNeverRequestsWrites()
    {
        using var temp = new TemporaryDirectory();
        var process = new FakeGitProcess(temp.Root);
        var status = await new GitClient(process).GetStatusAsync(temp.Root);
        Assert.True(status.IsRepository);
        Assert.Equal("main", status.Branch);
        Assert.Equal(2, process.Calls.Count);
        Assert.Equal(new[] { "rev-parse", "--show-toplevel" }, process.Calls[0]);
        Assert.Equal(new[] { "status", "--porcelain=v1", "-z", "--branch", "--untracked-files=all" }, process.Calls[1]);
    }

    [Fact]
    public async Task FailedStatusNeverReportsACleanRepository()
    {
        using var temp = new TemporaryDirectory();
        var status = await new GitClient(new FailedStatusProcess(temp.Root)).GetStatusAsync(temp.Root);
        Assert.True(status.IsRepository);
        Assert.False(status.IsStatusKnown);
        Assert.DoesNotContain("clean", status.Summary);
        Assert.Equal("Git status unavailable", status.Summary);
        Assert.NotEmpty(status.Warnings);
    }

    [Fact]
    public async Task RealGitReportsUntrackedSemanticFileThenCleanCommit()
    {
        using var temp = new TemporaryDirectory();
        await RunGit(temp.Root, "init", "-b", "main");
        temp.Write("Sales.SemanticModel/definition/tables/Größe.tmdl", "table Größe");
        var dirty = await new GitClient().GetStatusAsync(temp.Root);
        Assert.Equal("main", dirty.Branch);
        Assert.True(dirty.IsDirty);
        Assert.Equal(new[] { "Sales.SemanticModel/definition/tables/Größe.tmdl" }, dirty.ChangedSemanticFiles);
        await RunGit(temp.Root, "add", "--all");
        await RunGit(temp.Root, "-c", "user.name=PbiBench test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false", "commit", "-m", "Fixture");
        Assert.False((await new GitClient().GetStatusAsync(temp.Root)).IsDirty);
    }

    [Fact]
    public async Task RealGitHandlesNonRepositoryWithoutThrowing()
    {
        using var temp = new TemporaryDirectory();
        var status = await new GitClient().GetStatusAsync(temp.Root);
        Assert.False(status.IsRepository);
        Assert.NotEmpty(status.Warnings);
    }

    [Fact]
    public async Task CancellationStopsBeforeStartingGit()
    {
        using var temp = new TemporaryDirectory();
        var process = new FakeGitProcess(temp.Root);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GitClient(process).GetStatusAsync(temp.Root, ct: cancellation.Token));
        Assert.Empty(process.Calls);
    }

    private static async Task RunGit(string root, params string[] args)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("git", string.Join(" ", args.Select(WindowsCommandLine.Quote)))
        { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await Task.Run(() => process.WaitForExit());
        await output;
        Assert.True(process.ExitCode == 0, await error);
    }

    private sealed class FakeGitProcess(string root) : IGitProcessRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = new();
        public Task<GitResult> RunAsync(string directory, IReadOnlyList<string> arguments, CancellationToken ct = default)
        {
            Calls.Add(arguments);
            return Task.FromResult(new GitResult(0, arguments[0] == "rev-parse" ? root + "\n" : "## main\0", ""));
        }
    }

    private sealed class FailedStatusProcess(string root) : IGitProcessRunner
    {
        public Task<GitResult> RunAsync(string directory, IReadOnlyList<string> arguments, CancellationToken ct = default)
            => Task.FromResult(arguments[0] == "rev-parse" ? new GitResult(0, root + "\n", "") : new GitResult(128, "", "status failed"));
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    private static readonly string TestRoot = Path.Combine(Path.GetTempPath(), "PbiBench.Adapter.Tests");
    public string Root { get; } = Path.GetFullPath(Path.Combine(TestRoot, Guid.NewGuid().ToString("N")));
    public TemporaryDirectory() => Directory.CreateDirectory(Root);
    public string Write(string relative, string content)
    {
        var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
    public void Dispose()
    {
        if (!Root.StartsWith(Path.GetFullPath(TestRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to remove a directory outside the test fixture root.");
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(Root, true);
    }
}
