using NawiriAI.Abstractions;
using NawiriAI.Core;

namespace NawiriAI.Core.Tests;

public class IntelligenceTests
{
    private static readonly BusinessSecurityContext Scope = new("synthetic-tenant", ["demo-branch"], "demo-branch", "demo-user", ["sales.read"], "test-request");
    private static readonly BusinessQuery Query = new(BusinessMetric.SalesSummary, new(new(2026, 9, 1), new(2026, 9, 12)));

    [Fact]
    public async Task ProviderFailurePreservesTrustedFigures()
    {
        var provider = new TestProvider { Fail = true };
        var answer = await new IntelligenceService(new TestData(), new ProviderRouter([provider]))
            .QueryAsync(Query, Scope, new("test", "configured", DataSharingApproved: true));
        Assert.Equal(1500m, answer.Facts.Measures[0].Value);
        Assert.Contains("1,500.00", answer.DisplayText);
        Assert.Equal("unavailable", answer.ProviderStatus);
    }

    [Fact]
    public async Task ProviderGetsMinimalFactsWithoutHostIdentifiersOrUserPrompt()
    {
        var provider = new TestProvider();
        var answer = await new IntelligenceService(new TestData(), new ProviderRouter([provider]))
            .QueryAsync(Query, Scope, new("test", "configured", DataSharingApproved: true));
        Assert.NotNull(provider.Request);
        var payload = string.Join(" ", provider.Request.Messages.Select(m => m.Content));
        Assert.DoesNotContain("synthetic-tenant", payload);
        Assert.DoesNotContain("demo-user", payload);
        Assert.DoesNotContain("demo-branch", payload);
        Assert.Contains("1500", payload);
        Assert.Equal(1500m, answer.Facts.Measures[0].Value);
    }

    [Fact]
    public async Task NoConsentMeansNoProviderCall()
    {
        var provider = new TestProvider();
        var answer = await new IntelligenceService(new TestData(), new ProviderRouter([provider]))
            .QueryAsync(Query, Scope, new("test", "configured"));
        Assert.Null(provider.Request);
        Assert.Equal("not-approved", answer.ProviderStatus);
    }

    [Fact]
    public async Task WrongTenantResultNeverReachesProvider()
    {
        var provider = new TestProvider();
        await Assert.ThrowsAsync<BusinessAccessException>(() => new IntelligenceService(new TestData { WrongScope = true }, new ProviderRouter([provider]))
            .QueryAsync(Query, Scope, new("test", "configured", DataSharingApproved: true)));
        Assert.Null(provider.Request);
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new IntelligenceService(new TestData(), new ProviderRouter([]))
            .QueryAsync(Query, Scope, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task AuditOutageDoesNotReplaceTrustedAnswer()
    {
        var answer = await new IntelligenceService(new TestData(), new ProviderRouter([]), audit: new BrokenAudit())
            .QueryAsync(Query, Scope);
        Assert.Equal(1500m, answer.Facts.Measures[0].Value);
    }

    [Fact]
    public async Task AuditOutageDoesNotReplaceAccessDenial()
    {
        var denied = new BusinessSecurityContext("tenant", ["branch"], "branch", "user", [], "request");
        await Assert.ThrowsAsync<BusinessAccessException>(() => new IntelligenceService(new TestData(), new ProviderRouter([]), audit: new BrokenAudit())
            .QueryAsync(Query, denied));
    }

    private sealed class BrokenAudit : IAIAuditSink
    {
        public Task WriteAsync(AIAuditEvent auditEvent, CancellationToken cancellationToken = default) => throw new IOException("Sink unavailable");
    }

    private sealed class TestData : IBusinessDataProvider
    {
        public bool WrongScope { get; init; }
        public IReadOnlySet<BusinessMetric> SupportedMetrics { get; } = new HashSet<BusinessMetric> { BusinessMetric.SalesSummary };
        public Task<BusinessDataResult> ExecuteAsync(BusinessQuery query, BusinessSecurityContext security, CancellationToken cancellationToken = default)
            => Task.FromResult(new BusinessDataResult(query.Metric, WrongScope ? security.Scope with { TenantId = "other" } : security.Scope,
                query.Period, [new("Revenue", 1500m, "KES")], [], "synthetic-sales", DateTimeOffset.UtcNow));
    }
    private sealed class TestProvider : IAIProvider
    {
        public bool Fail { get; init; }
        public AIRequest? Request { get; private set; }
        public string Id => "test";
        public string DisplayName => "Test";
        public AIProviderCapabilities Capabilities => new(false, false, false);
        public Task<AIConnectionResult> TestConnectionAsync(AIProviderConfiguration configuration, CancellationToken cancellationToken = default) => Task.FromResult(new AIConnectionResult(true, "mock"));
        public Task<AIResponse> CompleteAsync(AIRequest request, AIProviderConfiguration configuration, CancellationToken cancellationToken = default)
        {
            Request = request;
            if (Fail) throw new AIProviderException("Unavailable");
            return Task.FromResult(new AIResponse("Review your verified sales figures.", Id, configuration.ModelId));
        }
    }
}
