using NawiriAI.Abstractions;

namespace NawiriAI.Api;

public static class DemoProviderConfiguration
{
    public static AIProviderConfiguration? Read(IConfiguration configuration)
    {
        var id = configuration["NAWIRIAI_PROVIDER"]?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(id) || id is "none" or "mock") return null;

        Uri? endpoint = null;
        var configuredEndpoint = configuration["NAWIRIAI_ENDPOINT"];
        if (!string.IsNullOrEmpty(configuredEndpoint))
        {
            if (!Uri.TryCreate(configuredEndpoint, UriKind.Absolute, out endpoint)
                || !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query)
                || !string.IsNullOrEmpty(endpoint.Fragment)
                || (endpoint.Scheme != Uri.UriSchemeHttps
                    && !(id == "local" && endpoint.IsLoopback && endpoint.Scheme == Uri.UriSchemeHttp)))
                throw new AIProviderException("The configured provider endpoint is invalid. Use a complete HTTPS inference URL, or loopback HTTP for a local server, without URL credentials or parameters.");
        }

        var localLoopback = id == "local" && (endpoint is null || endpoint.IsLoopback);
        var keyConfigured = !string.IsNullOrWhiteSpace(configuration["NAWIRIAI_API_KEY"]);
        var secretReference = localLoopback && !keyConfigured ? null : "demo-provider-key";
        return new(id, configuration["NAWIRIAI_MODEL"] ?? "", secretReference, endpoint,
            DataSharingApproved: bool.TryParse(configuration["NAWIRIAI_DATA_SHARING_APPROVED"], out var approved) && approved);
    }
}
