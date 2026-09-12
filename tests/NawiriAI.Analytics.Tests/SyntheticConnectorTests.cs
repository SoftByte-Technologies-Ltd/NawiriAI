using NawiriAI.Abstractions;
using NawiriAI.Connectors;
using NawiriAI.Core;

namespace NawiriAI.Analytics.Tests;

public class SyntheticConnectorTests
{
    private static readonly DateOnly Today = new(2026, 9, 12);
    private readonly SyntheticBusinessDataProvider provider = new(Today);
    private static BusinessSecurityContext Context(string? tenant = null, string? branch = null,
        string? user = null, IEnumerable<string>? permissions = null) => new(
            tenant ?? SyntheticBusinessDataProvider.SampleTenant,
            [SyntheticBusinessDataProvider.SampleBranch, SyntheticBusinessDataProvider.SecondBranch],
            branch ?? SyntheticBusinessDataProvider.SampleBranch,
            user ?? SyntheticBusinessDataProvider.SampleUser,
            permissions ?? ["sales.read", "inventory.read", "customers.read", "expenses.read", "business.read", "staff.read"], "test-run");
    private static BusinessQuery Query(BusinessMetric metric, int limit = 10) => new(metric, new(Today.AddDays(-6), Today), limit,
        metric == BusinessMetric.PeriodComparison ? new(Today.AddDays(-13), Today.AddDays(-7)) : null);
    private static decimal Measure(BusinessDataResult result, string label) => Assert.Single(result.Measures, m => m.Name == label).Value;

    [Theory]
    [InlineData("foreign-tenant", "demo-main", "demo-owner")]
    [InlineData("demo-other-company", "demo-main", "demo-owner")]
    [InlineData("demo-company", "unknown-branch", "demo-owner")]
    [InlineData("demo-company", "demo-main", "unknown-user")]
    [InlineData("demo-company", "demo-outlet", "demo-main-reader")]
    public async Task Rejects_forged_company_branch_or_user_on_every_tool(string tenant, string branch, string user)
    {
        foreach (var metric in provider.SupportedMetrics)
            await Assert.ThrowsAsync<BusinessAccessException>(() => provider.ExecuteAsync(Query(metric), Context(tenant, branch, user)));
    }

    [Fact]
    public async Task Direct_connector_calls_validate_permissions_ranges_limits_and_cancellation()
    {
        await Assert.ThrowsAsync<BusinessAccessException>(() => provider.ExecuteAsync(Query(BusinessMetric.SalesSummary), Context(permissions: [])));
        await Assert.ThrowsAsync<BusinessAccessException>(() => provider.ExecuteAsync(Query(BusinessMetric.BusinessHealth), Context(permissions: ["business.read"])));
        await Assert.ThrowsAsync<BusinessAccessException>(() => provider.ExecuteAsync(Query(BusinessMetric.ExpensesSummary), Context(user: "demo-main-reader")));
        await Assert.ThrowsAsync<BusinessQueryException>(() => provider.ExecuteAsync(Query(BusinessMetric.SalesSummary, 101), Context()));
        await Assert.ThrowsAsync<BusinessQueryException>(() => provider.ExecuteAsync(new(BusinessMetric.SalesSummary, new(Today.AddDays(-366), Today)), Context()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.ExecuteAsync(Query(BusinessMetric.SalesSummary), Context(), new(true)));
    }

    [Fact]
    public async Task Aggregates_filter_branch_before_summing_and_top_rows_reconcile_to_sales()
    {
        var sales = await provider.ExecuteAsync(Query(BusinessMetric.SalesSummary), Context());
        var top = await provider.ExecuteAsync(Query(BusinessMetric.TopProducts), Context());
        Assert.Equal(Measure(sales, "Sales revenue"), top.Rows.Sum(r => Assert.Single(r.Measures, m => m.Name == "Revenue").Value));
        Assert.Equal(21m, Measure(sales, "Transactions"));
        Assert.True(Measure(sales, "Sales revenue") < 1000000m);
        var outlet = await provider.ExecuteAsync(Query(BusinessMetric.SalesSummary), Context(branch: SyntheticBusinessDataProvider.SecondBranch));
        Assert.Equal(7m, Measure(outlet, "Transactions"));
        Assert.NotEqual(Measure(sales, "Sales revenue"), Measure(outlet, "Sales revenue"));
        Assert.Equal(SyntheticBusinessDataProvider.SampleBranch, sales.Scope.BranchId);
    }

    [Fact]
    public async Task Payments_and_current_balances_do_not_include_other_tenant_sentinels_or_other_branches()
    {
        var sales = await provider.ExecuteAsync(Query(BusinessMetric.SalesSummary), Context());
        var payments = await provider.ExecuteAsync(Query(BusinessMetric.PaymentMix), Context());
        Assert.Equal(Measure(sales, "Sales revenue"), Measure(payments, "Payments received"));
        var inventory = await provider.ExecuteAsync(Query(BusinessMetric.InventorySummary), Context());
        Assert.Equal(88m, Measure(inventory, "Current stock units"));
        Assert.Equal(9650m, Measure(inventory, "Current stock at unit cost"));
        Assert.Equal(3m, Measure(inventory, "Items at or below reorder level"));
        var receivables = await provider.ExecuteAsync(Query(BusinessMetric.Receivables), Context());
        Assert.Equal(4200m, Measure(receivables, "Current receivables"));
        var expenses = await provider.ExecuteAsync(Query(BusinessMetric.ExpensesSummary), Context());
        Assert.Equal(280m, Measure(expenses, "Recorded expenses"));
    }

    [Fact]
    public async Task Documented_fixed_date_demo_has_stable_sales_and_period_comparison_totals()
    {
        var today = await provider.ExecuteAsync(new(BusinessMetric.SalesSummary, new(Today, Today)), Context());
        Assert.Equal(3790m, Measure(today, "Sales revenue"));
        var compared = await provider.ExecuteAsync(new(BusinessMetric.PeriodComparison, new(new(2026, 9, 1), Today), ComparisonPeriod: new(new(2026, 8, 1), new(2026, 8, 31))), Context());
        Assert.Equal(30560m, Measure(compared, "Current sales"));
        Assert.Equal(74870m, Measure(compared, "Previous sales"));
        Assert.Equal(-44310m, Measure(compared, "Sales change"));
    }

    [Fact]
    public async Task Every_advertised_tool_respects_limit_and_reports_real_data()
    {
        foreach (var metric in provider.SupportedMetrics)
        {
            var result = await provider.ExecuteAsync(Query(metric, 1), Context());
            Assert.Equal(metric, result.Metric);
            Assert.Null(result.CapabilityMessage);
            Assert.True(result.Rows.Count <= 1, metric.ToString());
            Assert.Contains("Synthetic", result.Source);
            Assert.True(result.Measures.Count + result.Rows.Count > 0, metric.ToString());
        }
    }

    [Fact]
    public async Task Unknown_capabilities_and_unavailable_historical_snapshots_are_truthful()
    {
        await Assert.ThrowsAsync<BusinessCapabilityException>(() => provider.ExecuteAsync(Query(BusinessMetric.StaffPerformance), Context()));
        await Assert.ThrowsAsync<BusinessCapabilityException>(() => provider.ExecuteAsync(new(BusinessMetric.InventorySummary, new(Today.AddDays(-1), Today.AddDays(-1))), Context()));
        await Assert.ThrowsAsync<BusinessCapabilityException>(() => provider.ExecuteAsync(new(BusinessMetric.SalesSummary, new(Today.AddDays(-200), Today.AddDays(-190))), Context()));
    }

    [Fact]
    public async Task Comparison_measures_match_independent_queries_and_default_seed_is_reproducible()
    {
        var query = Query(BusinessMetric.PeriodComparison);
        var compared = await provider.ExecuteAsync(query, Context());
        var current = await provider.ExecuteAsync(query with { Metric = BusinessMetric.SalesSummary, ComparisonPeriod = null }, Context());
        var previous = await provider.ExecuteAsync(new(BusinessMetric.SalesSummary, query.ComparisonPeriod!), Context());
        Assert.Equal(Measure(current, "Sales revenue"), Measure(compared, "Current sales"));
        Assert.Equal(Measure(previous, "Sales revenue"), Measure(compared, "Previous sales"));
        var repeated = await new SyntheticBusinessDataProvider(Today).ExecuteAsync(Query(BusinessMetric.SalesSummary), Context());
        Assert.Equal(current.Measures, repeated.Measures);
    }

    [Fact]
    public async Task End_to_end_required_questions_work_without_any_model_or_network()
    {
        var service = new IntelligenceService(provider, new ProviderRouter([]), clock: new FixedClock(Today));
        foreach (var question in new[] { "How much did we sell today?", "Show top 5 products this month", "Compare sales this month to last month" })
        {
            var answer = await service.AskAsync(question, SyntheticBusinessDataProvider.CreateDemoContext("demo-question"));
            Assert.Equal("not-configured", answer.ProviderStatus);
            Assert.Null(answer.Narrative);
            Assert.NotEmpty(answer.DisplayText);
        }
    }

    private sealed class FixedClock(DateOnly today) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(today.ToDateTime(new(12, 0)), TimeSpan.Zero); }

    [Fact]
    public async Task Historical_daily_brief_returns_transactions_without_inventing_old_snapshots()
    {
        var period = new DateRange(Today.AddDays(-1), Today.AddDays(-1));
        var brief = await provider.ExecuteAsync(new(BusinessMetric.DailyBrief, period), Context());
        var sales = await provider.ExecuteAsync(new(BusinessMetric.SalesSummary, period), Context());
        Assert.Equal(Measure(sales, "Sales revenue"), Measure(brief, "Sales revenue"));
        Assert.DoesNotContain(brief.Measures, m => m.Name.Contains("Current receivables"));
        Assert.Contains(brief.Rows, row => row.Label.Contains("Historical inventory"));
    }
}
