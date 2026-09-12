using NawiriAI.Analytics;

namespace NawiriAI.Analytics.Tests;

public class AnalyticsTests
{
    [Fact]
    public void Comparison_uses_previous_period_as_denominator_and_preserves_zero_baseline()
    {
        var growth = PeriodAnalytics.Compare(150m, 100m);
        Assert.Equal(50m, growth.Change);
        Assert.Equal(50m, growth.ChangePercent);
        Assert.Null(PeriodAnalytics.Compare(100m, 0m).ChangePercent);
        Assert.Equal(-100m, PeriodAnalytics.Compare(0m, 100m).ChangePercent);
    }

    [Fact]
    public void Anomaly_requires_history_and_flags_a_large_departure_from_constant_baseline()
    {
        Assert.False(AnomalyAnalytics.Assess(1000m, [100m, 100m]).HasEnoughHistory);
        Assert.True(AnomalyAnalytics.Assess(1000m, [100m, 100m, 100m, 100m, 100m]).IsAnomaly);
        Assert.False(AnomalyAnalytics.Assess(110m, [100m, 100m, 100m, 100m, 100m]).IsAnomaly);
    }

    [Fact]
    public void Business_health_explains_observed_conditions_without_a_proprietary_score()
    {
        var result = BusinessHealthAnalytics.Assess(1000m, 600m, 500m, 2, 50m);
        Assert.Equal("attention", result.Status);
        Assert.Contains(result.Observations, text => text.Contains("expenses exceed gross profit"));
        Assert.Contains(result.Observations, text => text.Contains("reorder"));
        Assert.Equal("insufficient-data", BusinessHealthAnalytics.Assess(0, 0, 0, 0, 0).Status);
    }
}
