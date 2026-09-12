using Microsoft.Extensions.Configuration;
using NawiriAI.Abstractions;

namespace NawiriAI.Api.Tests;

public class ProviderConfigurationTests
{
    [Fact]
    public void MalformedApprovedEndpointDoesNotFallbackToAnotherDestination()
    {
        var config = Config("openai", "not-an-absolute-url");
        Assert.Throws<AIProviderException>(() => DemoProviderConfiguration.Read(config));
    }

    [Fact]
    public void LocalLoopbackCanRunWithoutCredentialReference()
    {
        var config = DemoProviderConfiguration.Read(Config("local", "http://localhost:11434/v1/chat/completions"));
        Assert.NotNull(config);
        Assert.Null(config.SecretReference);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("mock")]
    [InlineData("NONE")]
    [InlineData(" MOCK ")]
    [InlineData("")]
    public void DisabledProviderDoesNotCreateConfiguration(string provider)
    {
        Assert.Null(DemoProviderConfiguration.Read(Config(provider, "unused-invalid-endpoint")));
    }

    [Theory]
    [InlineData("https://user:private-credential@example.com/v1/responses")]
    [InlineData("https://example.com/v1/responses?api_key=private-credential")]
    [InlineData("https://example.com/v1/responses#private-credential")]
    [InlineData("http://remote.example/v1/responses")]
    [InlineData("file:///private-credential")]
    [InlineData(" ")]
    public void UnsafeSuppliedEndpointsFailWithSafeMessage(string endpoint)
    {
        var exception = Assert.Throws<AIProviderException>(() => DemoProviderConfiguration.Read(Config("openai", endpoint)));
        Assert.DoesNotContain("private-credential", exception.ToString());
    }

    [Fact]
    public void ValidCustomEndpointIsPreservedExactly()
    {
        const string endpoint = "https://approved.example/custom/responses";
        Assert.Equal(endpoint, DemoProviderConfiguration.Read(Config("openai", endpoint))!.Endpoint!.AbsoluteUri);
    }

    [Fact]
    public void LocalConfiguredKeyUsesVaultReferenceOnly()
    {
        var settings = Config("local", "http://127.0.0.1:1234/v1/chat/completions");
        settings["NAWIRIAI_API_KEY"] = "synthetic-test-only-key";
        var configuration = DemoProviderConfiguration.Read(settings);
        Assert.Equal("demo-provider-key", configuration!.SecretReference);
        Assert.DoesNotContain("synthetic-test-only-key", configuration.ToString());
    }

    [Fact]
    public void LocalDefaultEndpointCanRunWithoutKey()
    {
        var settings = Config("local", "http://localhost:11434/v1/chat/completions");
        settings["NAWIRIAI_ENDPOINT"] = null;
        Assert.Null(DemoProviderConfiguration.Read(settings)!.SecretReference);
    }

    private static IConfiguration Config(string provider, string endpoint) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["NAWIRIAI_PROVIDER"] = provider,
        ["NAWIRIAI_MODEL"] = "configured-model",
        ["NAWIRIAI_ENDPOINT"] = endpoint,
        ["NAWIRIAI_DATA_SHARING_APPROVED"] = "true"
    }).Build();
}
