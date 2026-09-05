using Microsoft.Identity.Client;
using PbiBench.Core.Abstractions;

namespace PbiBench.Fabric;

public enum FabricAudience { Fabric, OneLake, Sql, PowerBi }
public sealed record FabricSignInOptions(string TenantId, string ClientId)
{
    public void Validate()
    {
        if (!Guid.TryParse(TenantId, out var tenant) || tenant == Guid.Empty) throw new ArgumentException("Enter the Entra tenant GUID for your organization.");
        if (!Guid.TryParse(ClientId, out var client) || client == Guid.Empty) throw new ArgumentException("Enter your registered public-client application GUID.");
    }
}
public interface IFabricAuthenticator : IAccessTokenProvider
{
    string? AccountLabel { get; }
    Task SignInAsync(FabricSignInOptions options, FabricAudience audience, CancellationToken cancellationToken);
    Task SignOutAsync(CancellationToken cancellationToken);
}
public sealed class FabricAuthenticationRequiredException(string message) : Exception(message);

/// <summary>MSAL public-client flow. Interactive acquisition happens only through SignInAsync; caches remain in memory.</summary>
public sealed class EntraPublicClientTokenProvider : IFabricAuthenticator
{
    private IPublicClientApplication? application;
    private IAccount? account;
    private FabricSignInOptions? configured;
    private readonly SemaphoreSlim gate = new(1, 1);
    public string? AccountLabel => account?.Username;
    public static string[] Scopes(FabricAudience audience) => audience switch
    {
        FabricAudience.Fabric => new[] { "https://api.fabric.microsoft.com/.default" },
        FabricAudience.OneLake => new[] { "https://storage.azure.com/.default" },
        FabricAudience.Sql => new[] { "https://database.windows.net/.default" },
        FabricAudience.PowerBi => new[] { "https://analysis.windows.net/powerbi/api/.default" },
        _ => throw new ArgumentOutOfRangeException(nameof(audience))
    };
    public async Task SignInAsync(FabricSignInOptions options, FabricAudience audience, CancellationToken cancellationToken)
    {
        options.Validate(); var scopes = Scopes(audience); await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (configured != options)
            {
                application = PublicClientApplicationBuilder.Create(options.ClientId)
                    .WithAuthority("https://login.microsoftonline.com/" + options.TenantId)
                    .WithRedirectUri("http://localhost").Build();
                account = null; configured = options;
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromMinutes(5));
            var builder = application!.AcquireTokenInteractive(scopes).WithUseEmbeddedWebView(false);
            if (account != null) builder = builder.WithAccount(account);
            var result = await builder.ExecuteAsync(deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            if (account != null && account.HomeAccountId.Identifier != result.Account.HomeAccountId.Identifier)
                throw new FabricAuthenticationRequiredException("A different account was selected. Sign out before changing Fabric accounts.");
            account = result.Account;
        }
        catch (MsalException) { throw new FabricAuthenticationRequiredException("Entra sign-in did not complete. Check the tenant, public-client registration, redirect URI, API consent, and organizational access policy."); }
        finally { gate.Release(); }
    }
    public async Task<string> GetAccessTokenAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken = default)
    {
        var requested = scopes.ToArray();
        if (requested.Length != 1 || !Enum.GetValues(typeof(FabricAudience)).Cast<FabricAudience>().Any(audience => Scopes(audience).SequenceEqual(requested)))
            throw new ArgumentException("Only the configured Fabric, OneLake, SQL, and Power BI resource scopes are accepted.");
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (application == null || account == null) throw new FabricAuthenticationRequiredException("Sign in to Fabric from the Fabric page first.");
            var result = await application.AcquireTokenSilent(requested, account).ExecuteAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested(); return result.AccessToken;
        }
        catch (MsalException) { throw new FabricAuthenticationRequiredException("This resource requires consent or a fresh sign-in. Use its Authorize button on the Fabric page."); }
        finally { gate.Release(); }
    }
    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (application != null) foreach (var item in await application.GetAccountsAsync().ConfigureAwait(false))
            { cancellationToken.ThrowIfCancellationRequested(); await application.RemoveAsync(item).ConfigureAwait(false); }
            account = null; application = null; configured = null;
        }
        finally { gate.Release(); }
    }
}
