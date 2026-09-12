using NawiriAI.Abstractions;
namespace NawiriAI.Core;

public static class ToolRegistry
{
    public static IReadOnlyList<BusinessTool> Tools { get; } = Array.AsReadOnly(Enum.GetValues<BusinessMetric>()
        .Select(metric => new BusinessTool(metric, Permission(metric), metric.ToString())).ToArray());

    public static string Permission(BusinessMetric metric) => metric switch
    {
        BusinessMetric.InventorySummary or BusinessMetric.LowStock or BusinessMetric.StockMovement or BusinessMetric.StockOutRisk => "inventory.read",
        BusinessMetric.CustomerSummary or BusinessMetric.TopCustomers or BusinessMetric.Receivables or BusinessMetric.ReceivablesAging => "customers.read",
        BusinessMetric.ExpensesSummary => "expenses.read",
        BusinessMetric.StaffPerformance => "staff.read",
        BusinessMetric.DailyBrief or BusinessMetric.BusinessHealth => "business.read",
        _ => "sales.read"
    };
}
