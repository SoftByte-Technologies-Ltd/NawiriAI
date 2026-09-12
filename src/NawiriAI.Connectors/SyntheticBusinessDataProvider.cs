using System.Collections.Frozen;
using NawiriAI.Abstractions;
using NawiriAI.Analytics;
using NawiriAI.Core;
namespace NawiriAI.Connectors;

/// <summary>In-memory fictional data only. Contains no business-system, database or model credentials.</summary>
public sealed class SyntheticBusinessDataProvider : IBusinessDataProvider
{
    public const string SampleTenant = "demo-company", SampleBranch = "demo-main", SampleUser = "demo-owner", SecondBranch = "demo-outlet";
    private static readonly string[] OwnerPermissions = ["sales.read", "inventory.read", "customers.read", "expenses.read", "business.read", "staff.read"];
    private static readonly FrozenSet<BusinessMetric> Metrics = Enum.GetValues<BusinessMetric>().Except(
        [BusinessMetric.StockMovement, BusinessMetric.StaffPerformance, BusinessMetric.DiscountsAnalysis, BusinessMetric.VoidsReturns]).ToFrozenSet();
    private static readonly FrozenSet<BusinessMetric> SnapshotMetrics = new[] {
        BusinessMetric.InventorySummary, BusinessMetric.LowStock, BusinessMetric.StockOutRisk,
        BusinessMetric.CustomerSummary, BusinessMetric.Receivables, BusinessMetric.ReceivablesAging,
        BusinessMetric.BusinessHealth }.ToFrozenSet();
    private readonly SyntheticDataset data;

    public SyntheticBusinessDataProvider(DateOnly today, int seed = 1729)
    {
        if (today.DayNumber < 119) throw new ArgumentOutOfRangeException(nameof(today));
        Today = today;
        data = SyntheticDataset.Create(today, seed);
    }
    public DateOnly Today { get; }
    public DateRange Coverage => new(Today.AddDays(-119), Today);
    public IReadOnlySet<BusinessMetric> SupportedMetrics => Metrics;
    public static BusinessSecurityContext CreateDemoContext(string correlationId) => new(SampleTenant,
        [SampleBranch, SecondBranch], SampleBranch, SampleUser, OwnerPermissions, correlationId, ["demo-owner"]);

    public Task<BusinessDataResult> ExecuteAsync(BusinessQuery query, BusinessSecurityContext security, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Authorize(query, security);
        if (!Metrics.Contains(query.Metric)) throw new BusinessCapabilityException("This tool has no records in the synthetic connector.");
        EnsureCoverage(query.Period);
        if (query.ComparisonPeriod is not null) EnsureCoverage(query.ComparisonPeriod);
        if (SnapshotMetrics.Contains(query.Metric) && query.Period.End != Today)
            throw new BusinessCapabilityException("The synthetic connector only has a current inventory and customer-balance snapshot; historical snapshots are unavailable.");

        // Restrict every source to tenant AND branch before joining or aggregating it.
        var scopedSales = data.Sales.Where(x => x.TenantId == security.TenantId && x.BranchId == security.ActiveBranchId).ToArray();
        var sales = scopedSales.Where(x => InPeriod(x.Date, query.Period)).ToArray();
        var products = data.Products.Where(x => x.TenantId == security.TenantId).ToDictionary(x => x.Id);
        var inventory = data.Inventory.Where(x => x.TenantId == security.TenantId && x.BranchId == security.ActiveBranchId).ToArray();
        var customers = data.Customers.Where(x => x.TenantId == security.TenantId && x.BranchId == security.ActiveBranchId).ToArray();
        var expenses = data.Expenses.Where(x => x.TenantId == security.TenantId && x.BranchId == security.ActiveBranchId && InPeriod(x.Date, query.Period)).ToArray();
        var payments = data.Payments.Where(x => x.TenantId == security.TenantId && x.BranchId == security.ActiveBranchId && InPeriod(x.Date, query.Period)).ToArray();
        var lines = sales.SelectMany(x => x.Items).ToArray();
        var revenue = sales.Sum(x => x.Revenue);
        var costs = sales.Sum(x => x.Cost);
        var measures = new List<BusinessMeasure>();
        IEnumerable<BusinessRow> rows = [];
        switch (query.Metric)
        {
            case BusinessMetric.SalesSummary:
                measures.AddRange([Money("Sales revenue", revenue), Count("Transactions", sales.Length), Count("Units sold", lines.Sum(x => x.Quantity)), Money("Average transaction", sales.Length == 0 ? 0 : revenue / sales.Length)]);
                break;
            case BusinessMetric.SalesTrend:
            case BusinessMetric.DailySales:
                measures.Add(Money("Sales revenue", revenue));
                rows = Enumerable.Range(0, query.Period.End.DayNumber - query.Period.Start.DayNumber + 1)
                    .Select(offset => query.Period.Start.AddDays(offset)).Select(day => Row(day.ToString("yyyy-MM-dd"),
                        Money("Revenue", sales.Where(x => x.Date == day).Sum(x => x.Revenue)), Count("Transactions", sales.Count(x => x.Date == day))));
                break;
            case BusinessMetric.TopProducts:
            case BusinessMetric.SlowMovingProducts:
                var productTotals = products.Values.Select(p => new { Product = p, Revenue = lines.Where(l => l.ProductId == p.Id).Sum(l => l.Revenue), Units = lines.Where(l => l.ProductId == p.Id).Sum(l => l.Quantity) });
                rows = (query.Metric == BusinessMetric.TopProducts ? productTotals.OrderByDescending(x => x.Revenue).ThenBy(x => x.Product.Id)
                    : productTotals.OrderBy(x => x.Units).ThenBy(x => x.Product.Id)).Select(x => Row(x.Product.Label, Money("Revenue", x.Revenue), Count("Units sold", x.Units)));
                measures.Add(Money("Sales revenue across all products", revenue));
                break;
            case BusinessMetric.CategoryPerformance:
            case BusinessMetric.CategoryProfitability:
                rows = lines.GroupBy(x => products[x.ProductId].Category).OrderByDescending(g => g.Sum(x => x.Revenue))
                    .Select(g => Row(g.Key, Money("Revenue", g.Sum(x => x.Revenue)), Money("Cost of goods", g.Sum(x => x.Cost)), Money("Gross profit", g.Sum(x => x.Revenue - x.Cost))));
                measures.Add(Money("Sales revenue", revenue));
                break;
            case BusinessMetric.GrossMargin:
                measures.AddRange([Money("Sales revenue", revenue), Money("Cost of goods", costs), Money("Gross profit", revenue - costs)]);
                if (revenue != 0) measures.Add(Percent("Gross margin", (revenue - costs) / revenue * 100m));
                break;
            case BusinessMetric.PeriodComparison:
                var previousSales = scopedSales.Where(x => InPeriod(x.Date, query.ComparisonPeriod!)).Sum(x => x.Revenue);
                var comparison = PeriodAnalytics.Compare(revenue, previousSales);
                measures.AddRange([Money("Current sales", comparison.Current), Money("Previous sales", comparison.Previous), Money("Sales change", comparison.Change)]);
                if (comparison.ChangePercent.HasValue) measures.Add(Percent("Sales change percentage", comparison.ChangePercent.Value));
                else rows = [Row("Percentage change is undefined because previous sales are zero.")];
                break;
            case BusinessMetric.InventorySummary:
            case BusinessMetric.LowStock:
            case BusinessMetric.StockOutRisk:
                measures.AddRange([Count("Current stock units", inventory.Sum(x => x.Quantity)), Money("Current stock at unit cost", inventory.Sum(x => x.Quantity * products[x.ProductId].UnitCost)), Count("Items at or below reorder level", inventory.Count(x => x.Quantity <= x.ReorderLevel))]);
                var inventorySelection = query.Metric == BusinessMetric.InventorySummary ? inventory : inventory.Where(x => x.Quantity <= x.ReorderLevel);
                rows = inventorySelection.OrderBy(x => x.Quantity).ThenBy(x => x.ProductId).Select(x =>
                {
                    var units = lines.Where(l => l.ProductId == x.ProductId).Sum(l => l.Quantity);
                    var values = new List<BusinessMeasure> { Count("Current units", x.Quantity), Count("Reorder level", x.ReorderLevel) };
                    if (query.Metric == BusinessMetric.StockOutRisk && units > 0)
                        values.Add(new("Estimated days of cover at selected-period sales rate", x.Quantity / (units / (query.Period.End.DayNumber - query.Period.Start.DayNumber + 1m)), "days"));
                    return new BusinessRow(products[x.ProductId].Label, Array.AsReadOnly(values.ToArray()));
                });
                break;
            case BusinessMetric.PaymentMix:
                measures.Add(Money("Payments received", payments.Sum(x => x.Amount)));
                rows = payments.GroupBy(x => x.Method).OrderBy(g => g.Key).Select(g => Row(g.Key, Money("Amount", g.Sum(x => x.Amount)), Count("Payments", g.Count())));
                break;
            case BusinessMetric.CustomerSummary:
                measures.AddRange([Count("Current customers", customers.Length), Count("Customers purchasing in period", sales.Select(x => x.CustomerId).Distinct().Count()), Money("Current receivables", customers.Sum(x => x.Balance))]);
                break;
            case BusinessMetric.TopCustomers:
                rows = sales.GroupBy(x => x.CustomerId).OrderByDescending(g => g.Sum(x => x.Revenue)).ThenBy(g => g.Key)
                    .Select(g => Row(customers.Single(c => c.Id == g.Key).Label, Money("Revenue", g.Sum(x => x.Revenue)), Count("Transactions", g.Count())));
                measures.Add(Money("Sales revenue", revenue));
                break;
            case BusinessMetric.Receivables:
            case BusinessMetric.ReceivablesAging:
                measures.Add(Money("Current receivables", customers.Sum(x => x.Balance)));
                rows = query.Metric == BusinessMetric.Receivables
                    ? customers.Where(x => x.Balance > 0).OrderByDescending(x => x.Balance).Select(x => Row(x.Label, Money("Outstanding balance", x.Balance), new("Days past due", Math.Max(0, Today.DayNumber - x.DueDate.DayNumber), "days")))
                    : customers.Where(x => x.Balance > 0).GroupBy(x => AgeBand(Today.DayNumber - x.DueDate.DayNumber)).OrderBy(g => g.Key).Select(g => Row(g.Key, Money("Outstanding balance", g.Sum(x => x.Balance))));
                break;
            case BusinessMetric.ExpensesSummary:
                measures.Add(Money("Recorded expenses", expenses.Sum(x => x.Amount)));
                rows = expenses.GroupBy(x => x.Category).OrderBy(g => g.Key).Select(g => Row(g.Key, Money("Amount", g.Sum(x => x.Amount))));
                break;
            case BusinessMetric.TransactionAnomalies:
                var assessed = sales.Select(s => new { Sale = s, Assessment = AnomalyAnalytics.Assess(s.Revenue,
                    scopedSales.Where(h => h.Date < s.Date && h.Date >= s.Date.AddDays(-7)).Select(h => h.Revenue).ToArray()) }).ToArray();
                var flagged = assessed.Where(x => x.Assessment.IsAnomaly).ToArray();
                measures.AddRange([Count("Transactions assessed", assessed.Count(x => x.Assessment.HasEnoughHistory)), Count("Transactions flagged for review", flagged.Length)]);
                rows = flagged.OrderByDescending(x => x.Sale.Revenue).Select(x => Row(x.Sale.Id + " (statistical flag; not proof of fraud)", Money("Transaction revenue", x.Sale.Revenue), Money("Prior seven-day transaction median", x.Assessment.Median!.Value)));
                break;
            case BusinessMetric.DailyBrief:
                measures.AddRange([Money("Sales revenue", revenue), Count("Transactions", sales.Length), Money("Gross profit", revenue - costs), Money("Recorded expenses", expenses.Sum(x => x.Amount))]);
                if (query.Period.End == Today)
                    measures.AddRange([Count("Current items at or below reorder level", inventory.Count(x => x.Quantity <= x.ReorderLevel)), Money("Current receivables", customers.Sum(x => x.Balance))]);
                else rows = [Row("Historical inventory and receivables snapshots are unavailable; this brief contains recorded transactions and expenses only.")];
                break;
            case BusinessMetric.BusinessHealth:
                var low = inventory.Count(x => x.Quantity <= x.ReorderLevel);
                var balances = customers.Sum(x => x.Balance);
                var expenseTotal = expenses.Sum(x => x.Amount);
                var health = BusinessHealthAnalytics.Assess(revenue, costs, expenseTotal, low, balances);
                measures.AddRange([Money("Sales revenue", revenue), Money("Gross profit", revenue - costs), Money("Recorded expenses", expenseTotal), Money("Gross profit less recorded expenses", revenue - costs - expenseTotal), Count("Current items at or below reorder level", low), Money("Current receivables", balances)]);
                rows = health.Observations.Select(text => Row(health.Status + ": " + text));
                break;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new BusinessDataResult(query.Metric, security.Scope, query.Period,
            Array.AsReadOnly(measures.ToArray()), Array.AsReadOnly(rows.Take(query.Limit).ToArray()),
            $"Synthetic demo generator; current snapshot {Today:yyyy-MM-dd}; KES; no production data", DateTimeOffset.UtcNow));
    }

    private static void Authorize(BusinessQuery query, BusinessSecurityContext context)
    {
        new QueryPolicy().Validate(query, context);
        if (context.TenantId != SampleTenant) throw new BusinessAccessException("The authenticated company is not registered with this connector.");
        var owner = context.UserId == SampleUser;
        var reader = context.UserId == "demo-main-reader";
        var registeredBranch = context.ActiveBranchId == SampleBranch || owner && context.ActiveBranchId == SecondBranch;
        if ((!owner && !reader) || !registeredBranch)
            throw new BusinessAccessException("The authenticated user cannot access this connector branch.");
        if (!owner && ToolRegistry.Permission(query.Metric) is not ("sales.read" or "inventory.read"))
            throw new BusinessAccessException("The connector's registered user permissions do not allow this tool.");
    }
    private void EnsureCoverage(DateRange period)
    {
        if (period.Start < Coverage.Start || period.End > Coverage.End)
            throw new BusinessCapabilityException($"Synthetic transaction history is available only from {Coverage.Start:yyyy-MM-dd} through {Coverage.End:yyyy-MM-dd}.");
    }
    private static bool InPeriod(DateOnly date, DateRange period) => date >= period.Start && date <= period.End;
    private static string AgeBand(int days) => days <= 0 ? "Current" : days <= 30 ? "01-30 days overdue" : days <= 60 ? "31-60 days overdue" : "61+ days overdue";
    private static BusinessMeasure Money(string name, decimal value) => new(name, value, "KES");
    private static BusinessMeasure Count(string name, decimal value) => new(name, value, "count");
    private static BusinessMeasure Percent(string name, decimal value) => new(name, value, "%");
    private static BusinessRow Row(string label, params BusinessMeasure[] values) => new(label, Array.AsReadOnly(values));
}
