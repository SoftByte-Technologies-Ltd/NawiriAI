using NawiriAI.Connectors;

namespace NawiriAI.Analytics.Tests;

public class EnvironmentSecretStoreTests
{
    [Fact]
    public async Task Resolves_only_explicit_reference_mapping_and_snapshots_the_allowlist()
    {
        const string name = "NAWIRIAI_TEST_SYNTHETIC_SECRET";
        var previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, "fictional-test-value");
            var references = new Dictionary<string, string> { ["provider-reference"] = name };
            var store = new EnvironmentSecretStore(references);
            references["not-allowed"] = name;
            Assert.Equal("fictional-test-value", await store.GetSecretAsync("provider-reference"));
            Assert.Null(await store.GetSecretAsync(name));
            Assert.Null(await store.GetSecretAsync("PATH"));
            Assert.Null(await store.GetSecretAsync("not-allowed"));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await store.GetSecretAsync("provider-reference", new(true)));
        }
        finally { Environment.SetEnvironmentVariable(name, previous); }
    }

    [Fact]
    public void Rejects_invalid_environment_names_without_echoing_secret_like_inputs()
    {
        var error = Assert.Throws<ArgumentException>(() => new EnvironmentSecretStore(new Dictionary<string, string> { ["provider-reference"] = "x=fictional-value" }));
        Assert.DoesNotContain("fictional-value", error.Message);
    }
}
