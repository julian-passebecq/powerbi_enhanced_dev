namespace PbiBench.Core.Abstractions;

public interface IAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken = default);
}
