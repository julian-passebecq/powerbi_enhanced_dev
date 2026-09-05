using System.Text;
using System.Text.Json;
using PbiBench.CSharp.LanguageService;
using Xunit;

namespace PbiBench.V11.Tests;

public sealed class ScriptFileTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "PbiBench-script-" + Guid.NewGuid().ToString("N"));
    public ScriptFileTests() => Directory.CreateDirectory(directory);
    private string PathFor(string name = "source.csx") => Path.Combine(directory, name);
    public void Dispose() => Directory.Delete(directory, true);

    [Theory] [InlineData("source.cs")] [InlineData("source.csx")]
    public async Task SameLengthExternalChangeWithPreservedTimestampFailsClosed(string name)
    {
        var path = PathFor(name); File.WriteAllText(path, "original"); var stamp = File.GetLastWriteTimeUtc(path);
        var document = await ScriptWorkspaceFiles.OpenAsync(path, default);
        File.WriteAllText(path, "external"); File.SetLastWriteTimeUtc(path, stamp);
        var conflict = await Assert.ThrowsAsync<ScriptFileConflictException>(() => ScriptWorkspaceFiles.SaveAsync(document with { Text = "my edit" }, path, default));
        Assert.NotEqual(document.PersistedHash, conflict.ObservedHash); Assert.Equal("external", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }
    [Fact] public async Task UnchangedBytesAllowSaveDespiteTimestampChangeAndNewBaselineAllowsNextSave()
    {
        var path = PathFor(); File.WriteAllText(path, "original", Encoding.Unicode);
        var original = await ScriptWorkspaceFiles.OpenAsync(path, default); Assert.Equal("original", original.Text);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(1));
        var saved = await ScriptWorkspaceFiles.SaveAsync(original with { Text = "edit 日" }, path, default);
        Assert.False(saved.IsDirty); Assert.NotEqual(original.PersistedHash, saved.PersistedHash);
        saved = await ScriptWorkspaceFiles.SaveAsync(saved with { Text = "second" }, path, default);
        Assert.Equal("second", File.ReadAllText(path)); Assert.False(saved.IsDirty);
    }
    [Theory] [InlineData(1)] [InlineData(2)]
    public async Task RecoveryNeverRestoresFileAuthorityEvenWithPersistedHash(int schema)
    {
        var path = PathFor(); File.WriteAllText(path, "original");
        var document = (await ScriptWorkspaceFiles.OpenAsync(path, default)) with { Text = "recovered edit" };
        var recoveryPath = PathFor("recovery.json");
        await ScriptWorkspaceFiles.SaveRecoveryAsync(recoveryPath, new(new[] { document }, document.Id, schema), default);
        File.WriteAllText(path, "external");
        var recovered = Assert.Single((await ScriptWorkspaceFiles.LoadRecoveryAsync(recoveryPath, default)).Documents);
        Assert.Equal("recovered edit", recovered.Text); Assert.True(recovered.IsDirty); Assert.True(recovered.IsRecovered);
        Assert.Equal(path, recovered.RecoveredFrom); Assert.Null(recovered.FilePath); Assert.Null(recovered.PersistedHash); Assert.Equal("", recovered.SavedText);
        await Assert.ThrowsAsync<ScriptFileConflictException>(() => ScriptWorkspaceFiles.SaveAsync(recovered, path, default));
        Assert.Equal("external", File.ReadAllText(path));
        var savedAs = await ScriptWorkspaceFiles.SaveAsync(recovered, PathFor("new.csx"), default);
        Assert.False(savedAs.IsRecovered); Assert.False(savedAs.IsDirty); Assert.Equal(recovered.Text, File.ReadAllText(savedAs.FilePath!));
        await ScriptWorkspaceFiles.SaveRecoveryAsync(recoveryPath, new(new[] { recovered }, recovered.Id), default);
        Assert.Equal(path, (await ScriptWorkspaceFiles.LoadRecoveryAsync(recoveryPath, default)).Documents[0].RecoveredFrom);
    }
    [Fact] public async Task LegacyJsonRecoversTextWithoutRebinding()
    {
        var path = PathFor("recovery.json"); var id = Guid.NewGuid().ToString();
        File.WriteAllText(path, JsonSerializer.Serialize(new { SchemaVersion = 1, ActiveId = id, Documents = new[] { new { Id = id, Name = "a.csx", Text = "draft", SavedText = "old", FilePath = PathFor() } } }));
        var document = Assert.Single((await ScriptWorkspaceFiles.LoadRecoveryAsync(path, default)).Documents);
        Assert.Equal("draft", document.Text); Assert.True(document.IsRecovered); Assert.Null(document.FilePath);
    }
    [Fact] public async Task OverwriteReviewIsBoundToObservedBytesAndDestination()
    {
        var path = PathFor(); File.WriteAllText(path, "external"); var draft = new ScriptDocument(Guid.NewGuid().ToString(), "draft.csx", "my edit");
        var conflict = await Assert.ThrowsAsync<ScriptFileConflictException>(() => ScriptWorkspaceFiles.SaveAsync(draft, path, default));
        await Assert.ThrowsAsync<ArgumentException>(() => ScriptWorkspaceFiles.SaveAsync(draft, PathFor("other.cs"), default, conflict));
        File.WriteAllText(path, "changed again");
        var next = await Assert.ThrowsAsync<ScriptFileConflictException>(() => ScriptWorkspaceFiles.SaveAsync(draft, path, default, conflict));
        Assert.Equal("changed again", File.ReadAllText(path));
        var saved = await ScriptWorkspaceFiles.SaveAsync(draft, path, default, next); Assert.Equal(draft.Text, File.ReadAllText(path)); Assert.False(saved.IsDirty);
    }
    [Fact] public async Task DeletionIsAConflictAndExplicitRecreationRechecksAbsence()
    {
        var path = PathFor(); File.WriteAllText(path, "original"); var draft = await ScriptWorkspaceFiles.OpenAsync(path, default); File.Delete(path);
        var conflict = await Assert.ThrowsAsync<ScriptFileConflictException>(() => ScriptWorkspaceFiles.SaveAsync(draft, path, default));
        Assert.Null(conflict.ObservedHash); Assert.False(File.Exists(path));
        File.WriteAllText(path, "replacement");
        await Assert.ThrowsAsync<ScriptFileConflictException>(() => ScriptWorkspaceFiles.SaveAsync(draft, path, default, conflict)); Assert.Equal("replacement", File.ReadAllText(path));
        File.Delete(path); await ScriptWorkspaceFiles.SaveAsync(draft, path, default, conflict); Assert.Equal("original", File.ReadAllText(path));
    }
    [Fact] public async Task SaveAsCannotUseAnotherFilesBaselineAndReloadEstablishesANewOne()
    {
        var path = PathFor(); File.WriteAllText(path, "same"); var draft = await ScriptWorkspaceFiles.OpenAsync(path, default);
        var other = PathFor("other.cs"); File.WriteAllText(other, "same");
        await Assert.ThrowsAsync<ScriptFileConflictException>(() => ScriptWorkspaceFiles.SaveAsync(draft, other, default));
        File.WriteAllText(path, "reloaded"); var reload = await ScriptWorkspaceFiles.OpenAsync(path, default);
        Assert.Equal("reloaded", reload.Text); Assert.False(reload.IsDirty);
        await ScriptWorkspaceFiles.SaveAsync(reload with { Text = "next" }, path, default); Assert.Equal("next", File.ReadAllText(path));
    }
    [Fact] public async Task CancellationLockAndSizeFailurePreserveDestinationAndCleanStagingFiles()
    {
        var path = PathFor(); File.WriteAllText(path, "original"); var draft = (await ScriptWorkspaceFiles.OpenAsync(path, default)) with { Text = "edit" };
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ScriptWorkspaceFiles.SaveAsync(draft, path, canceled.Token));
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAnyAsync<IOException>(() => ScriptWorkspaceFiles.SaveAsync(draft, path, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => ScriptWorkspaceFiles.SaveAsync(draft with { Text = new string('日', 400000) }, path, default));
        Assert.Equal("original", File.ReadAllText(path)); Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }
    [Fact] public async Task RecoveryRejectsInvalidSchemasIdsAndBoundsWithoutReplacingGoodData()
    {
        var path = PathFor("recovery.json"); var draft = new ScriptDocument(Guid.NewGuid().ToString(), "a.csx", "draft"); var recovery = new ScriptRecovery(new[] { draft }, draft.Id);
        await ScriptWorkspaceFiles.SaveRecoveryAsync(path, recovery, default); var original = File.ReadAllText(path);
        foreach (var invalid in new[] { recovery with { SchemaVersion = 99 }, recovery with { ActiveId = "missing" }, recovery with { Documents = new[] { draft, draft } }, recovery with { Documents = Array.Empty<ScriptDocument>() }, recovery with { Documents = new[] { draft with { Text = new string('日', 400000) } } } })
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => ScriptWorkspaceFiles.SaveRecoveryAsync(path, invalid, default)); Assert.Equal(original, File.ReadAllText(path));
            File.WriteAllText(path, JsonSerializer.Serialize(invalid)); await Assert.ThrowsAsync<InvalidDataException>(() => ScriptWorkspaceFiles.LoadRecoveryAsync(path, default)); File.WriteAllText(path, original);
        }
        File.WriteAllBytes(path, new byte[8 * 1024 * 1024 + 1]); await Assert.ThrowsAsync<InvalidDataException>(() => ScriptWorkspaceFiles.LoadRecoveryAsync(path, default));
    }
}
