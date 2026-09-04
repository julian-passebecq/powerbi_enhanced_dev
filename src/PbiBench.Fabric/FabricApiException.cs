namespace PbiBench.Fabric;
public sealed class FabricApiException(string message, int statusCode, string? responseBody = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string? ResponseBody { get; } = responseBody;
}
