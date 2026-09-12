using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NawiriAI.Api.Tests;

public class ApiConfigurationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData("Pacific/Kiritimati")]
    [InlineData("Etc/GMT+12")]
    public async Task ChatTodayMatchesConfiguredBusinessDateAcrossTimeZones(string zone)
    {
        using var app = fixture.WithWebHostBuilder(builder => builder.UseSetting("NAWIRIAI_TIMEZONE", zone));
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Token);
        var response = await client.PostAsJsonAsync("/api/v1/chat", new { question = "What were my sales today?" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var period = body.RootElement.GetProperty("facts").GetProperty("period");
        Assert.Equal("2026-09-12", period.GetProperty("start").GetString());
        Assert.Equal("2026-09-12", period.GetProperty("end").GetString());
    }

    [Theory]
    [InlineData("2026-02-30")]
    [InlineData("yesterday")]
    [InlineData(" ")]
    public void ExplicitInvalidDemoDatePreventsStartup(string date)
    {
        using var app = fixture.WithWebHostBuilder(builder => builder.UseSetting("NAWIRIAI_DEMO_DATE", date));
        var exception = Assert.Throws<InvalidOperationException>(() => app.CreateClient());
        Assert.Contains("NAWIRIAI_DEMO_DATE", exception.Message);
    }

    [Fact]
    public async Task InvalidEndpointDoesNotReachDefaultProvider()
    {
        using var handler = new ProviderProbeHandler();
        using var providerClient = new HttpClient(handler);
        using var app = fixture.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("NAWIRIAI_PROVIDER", "openai");
            builder.UseSetting("NAWIRIAI_MODEL", "synthetic-test-model");
            builder.UseSetting("NAWIRIAI_ENDPOINT", "not-an-absolute-url");
            builder.UseSetting("NAWIRIAI_DATA_SHARING_APPROVED", "true");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<HttpClient>();
                services.AddSingleton(providerClient);
            });
        });
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Token);
        var response = await client.PostAsJsonAsync("/api/v1/chat", new { question = "Sales today" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task LocalEnrichmentWorksWithoutAConfiguredKey()
    {
        using var handler = new ProviderProbeHandler();
        using var providerClient = new HttpClient(handler);
        using var app = fixture.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("NAWIRIAI_PROVIDER", "local");
            builder.UseSetting("NAWIRIAI_MODEL", "synthetic-test-model");
            builder.UseSetting("NAWIRIAI_API_KEY", "");
            builder.UseSetting("NAWIRIAI_ENDPOINT", "http://127.0.0.1:11434/v1/chat/completions");
            builder.UseSetting("NAWIRIAI_DATA_SHARING_APPROVED", "true");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<HttpClient>();
                services.AddSingleton(providerClient);
            });
        });
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", fixture.Token);
        var response = await client.PostAsJsonAsync("/api/v1/chat", new { question = "Sales today" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, handler.Calls);
        Assert.Null(handler.Authorization);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("available", body.RootElement.GetProperty("providerStatus").GetString());
    }

    private sealed class ProviderProbeHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Authorization { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"Review the verified sales figures."},"finish_reason":"stop"}]}""", Encoding.UTF8, "application/json")
            });
        }
    }
}
