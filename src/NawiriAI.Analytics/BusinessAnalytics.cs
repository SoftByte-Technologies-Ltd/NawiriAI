namespace NawiriAI.Analytics;

public sealed record PeriodComparison(decimal Current, decimal Previous, decimal Change, decimal? ChangePercent);
public static class PeriodAnalytics
{
    /// <summary>Percentage uses the magnitude of the previous total. A zero baseline has no percentage.</summary>
    public static PeriodComparison Compare(decimal current, decimal previous) =>
        new(current, previous, current - previous, previous == 0 ? null : (current - previous) / Math.Abs(previous) * 100m);
}
public sealed record AnomalyAssessment(bool HasEnoughHistory, bool IsAnomaly, decimal? Median, decimal? DeviationThreshold);
public static class AnomalyAnalytics
{
    /// <summary>A descriptive median-absolute-deviation rule, not evidence of fraud. At least five observations are required.</summary>
    public static AnomalyAssessment Assess(decimal value, IReadOnlyList<decimal> history)
    {
        if (history.Count < 5) return new(false, false, null, null);
        var median = Median(history);
        var mad = Median(history.Select(x => Math.Abs(x - median)).ToArray());
        var threshold = Math.Max(3m * 1.4826m * mad, Math.Max(Math.Abs(median) * 0.25m, 1m));
        return new(true, Math.Abs(value - median) > threshold, median, threshold);
    }
    private static decimal Median(IReadOnlyList<decimal> observations)
    {
        var sorted = observations.Order().ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2m : sorted[middle];
    }
}
public sealed record BusinessHealthAssessment(string Status, IReadOnlyList<string> Observations);
public static class BusinessHealthAnalytics
{
    /// <summary>Transparent observations from supplied figures. No proprietary score or credit decision is computed.</summary>
    public static BusinessHealthAssessment Assess(decimal revenue, decimal costOfGoods, decimal expenses, int lowStockCount, decimal receivables)
    {
        var observations = new List<string>();
        if (revenue == 0) observations.Add("No sales were recorded in the selected period.");
        if (revenue < costOfGoods) observations.Add("Recorded cost of goods exceeds sales revenue.");
        if (expenses > revenue - costOfGoods) observations.Add("Recorded expenses exceed gross profit.");
        if (lowStockCount > 0) observations.Add("Some inventory items are at or below their reorder level.");
        if (receivables > 0) observations.Add("Customer balances remain outstanding in the current snapshot.");
        if (observations.Count == 0) observations.Add("No conditions were flagged by these simple checks.");
        var status = revenue == 0 ? "insufficient-data" : expenses > revenue - costOfGoods ? "attention"
            : lowStockCount > 0 || receivables > 0 ? "watch" : "stable";
        return new(status, Array.AsReadOnly(observations.ToArray()));
    }
}
