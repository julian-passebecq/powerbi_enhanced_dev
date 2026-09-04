using PbiBench.Core.Domain;
namespace PbiBench.Core.Abstractions;
public interface IAuditSink { Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default); }
