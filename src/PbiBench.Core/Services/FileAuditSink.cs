using System.Text.Json;
using PbiBench.Core.Abstractions;
using PbiBench.Core.Domain;
namespace PbiBench.Core.Services;

public sealed class FileAuditSink(string filePath) : IAuditSink
{
    private readonly SemaphoreSlim _gate = new(1,1);
    public async Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
            var line = JsonSerializer.Serialize(record) + Environment.NewLine;
            cancellationToken.ThrowIfCancellationRequested();
            using var writer = new StreamWriter(filePath, append: true);
            await writer.WriteAsync(line);
        }
        finally { _gate.Release(); }
    }
}
