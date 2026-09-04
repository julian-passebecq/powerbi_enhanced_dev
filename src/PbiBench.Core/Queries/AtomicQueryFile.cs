namespace PbiBench.Core.Queries;

/// <summary>Commits a fully written sibling file while preserving the previous destination on lock failure.</summary>
internal static class AtomicQueryFile
{
    internal static void Commit(string temporary, string destination, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
                return;
            }
            catch (IOException error) when (attempt < 7 && IsTransientLock(error) && File.Exists(temporary))
            {
                // Antivirus/indexers and readers without FileShare.Delete can briefly prevent
                // Windows ReplaceFile from removing the old name. Never delete it as a fallback.
                if (token.WaitHandle.WaitOne(Math.Min(400, 25 << attempt))) token.ThrowIfCancellationRequested();
            }
        }
    }

    private static bool IsTransientLock(IOException error)
    {
        var code = error.HResult & 0xFFFF;
        return code == 32 || code == 33 || code == 1175; // Sharing violation, lock violation, unable to remove replaced file.
    }
}
