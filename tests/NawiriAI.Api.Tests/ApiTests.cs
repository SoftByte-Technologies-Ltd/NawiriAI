using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NawiriAI.Api.Tests;

public sealed class ApiFixture : WebApplicationFactory<Program>
{
    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("NAWIRIAI_DEMO_MODE", "true");
        builder.UseSetting("NAWIRIAI_DEMO_TOKEN", Token);
        builder.UseSetting("NAWIRIAI_DEMO_DATE", "2026-09-12");
        builder.UseSetting("NAWIRIAI_TIMEZONE", "Africa/Nairobi");
        builder.UseSetting("NAWIRIAI_PROVIDER", "mock");
    }
    public HttpClient AuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return client;
    }
}

public class ApiTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task HealthContainsNoBusinessData()
    {
        using var client = fixture.CreateClient();
        var response = await client.GetAsync("/api/v1/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("synthetic-demo", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/v1/providers")]
    [InlineData("/api/v1/insights/daily")]
    [InlineData("/api/v1/business-health")]
    public async Task MissingTokenCannotReadBusinessData(string path)
    {
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task WrongTokenCannotChat()
    {
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "invalid-demo-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/chat", new { question = "sales today" })).StatusCode);
    }

    [Theory]
    [InlineData("What were my sales this month?")]
    [InlineData("What are my top 10 products this month?")]
    [InlineData("Compare sales this month with last month.")]
    public async Task MandatoryQuestionsReturnScopedFacts(string question)
    {
        using var client = fixture.AuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/v1/chat", new { question });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("demo-company", body.RootElement.GetProperty("facts").GetProperty("scope").GetProperty("tenantId").GetString());
        Assert.Equal("not-configured", body.RootElement.GetProperty("providerStatus").GetString());
        Assert.Contains("2026-09-12", body.RootElement.GetProperty("displayText").GetString());
    }

    [Theory]
    [InlineData("{\"question\":\"sales today\",\"branchId\":\"another\"}")]
    [InlineData("{\"question\":null}")]
    public async Task InvalidChatIsRejected(string body)
    {
        using var client = fixture.AuthenticatedClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/v1/chat", new StringContent(body, Encoding.UTF8, "application/json"))).StatusCode);
    }

    [Theory]
    [InlineData("{\"question\":\"sales today\",\"question\":\"sales yesterday\"}")]
    [InlineData("{\"question\":\"sales today\",\"Question\":\"sales yesterday\"}")]
    [InlineData("{\"Question\":\"sales today\",\"question\":\"sales yesterday\"}")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"question\":{\"text\":\"sales today\"}}")]
    public async Task ChatRequiresOneUnambiguousQuestion(string requestBody)
    {
        using var client = fixture.AuthenticatedClient();
        var response = await client.PostAsync("/api/v1/chat", new StringContent(requestBody, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("correlationId").GetString()));
    }

    [Theory]
    [InlineData("/api/v1/insights/daily", "2026-09-11", "2026-09-11")]
    [InlineData("/api/v1/business-health", "2026-09-01", "2026-09-12")]
    public async Task AuthenticatedSummaryEndpointsReturnExpectedPeriod(string path, string start, string end)
    {
        using var client = fixture.AuthenticatedClient();
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var period = body.RootElement.GetProperty("facts").GetProperty("period");
        Assert.Equal(start, period.GetProperty("start").GetString());
        Assert.Equal(end, period.GetProperty("end").GetString());
    }

    [Fact]
    public async Task QueryCannotChooseAnotherCompany()
    {
        using var client = fixture.AuthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/v1/query", new { metric = "SalesSummary", period = new { start = "2026-09-01", end = "2026-09-12" }, tenantId = "other" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(fixture.Token, await response.Content.ReadAsStringAsync());
    }
}
