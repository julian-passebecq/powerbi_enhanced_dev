using PbiBench.Core.Queries;
using Xunit;

namespace PbiBench.Adapters.Tests;

public sealed class QueryConnectionTargetTests
{
    [Theory]
    [InlineData("Data Source")]
    [InlineData("Server")]
    [InlineData("Addr")]
    [InlineData("Network Address")]
    public void RoutingEndpointIsPreservedWithoutAuthenticationOrDisplayName(string alias)
    {
        var endpoint = QueryConnectionTarget.Server(alias + "=powerbi://api.powerbi.com/v1.0/myorg/Engineering;User ID=user;Password=secret", "Display name");
        Assert.Equal("powerbi://api.powerbi.com/v1.0/myorg/Engineering", endpoint);
        Assert.DoesNotContain("secret", endpoint!);
    }
    [Fact]
    public void ConflictingAliasesAndMalformedInputNeverLeakTransportValues()
    {
        Assert.Throws<InvalidOperationException>(() => QueryConnectionTarget.Server("Server=one;Data Source=two"));
        var error = Assert.Throws<InvalidOperationException>(() => QueryConnectionTarget.Server("secret invalid input"));
        Assert.DoesNotContain("secret", error.ToString());
        Assert.Equal("localhost:12345", QueryConnectionTarget.Server("Application Name=PbiBench", "localhost:12345"));
    }
}
