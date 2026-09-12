using System.Collections.Frozen;

namespace NawiriAI.Abstractions;

public enum BusinessMetric
{
    SalesSummary, SalesTrend, DailySales, TopProducts, SlowMovingProducts,
    CategoryPerformance, CategoryProfitability, InventorySummary, LowStock,
    StockMovement, StockOutRisk, PaymentMix, CustomerSummary, TopCustomers,
    Receivables, ReceivablesAging, ExpensesSummary, GrossMargin, StaffPerformance,
    DiscountsAnalysis, VoidsReturns, TransactionAnomalies, PeriodComparison,
    DailyBrief, BusinessHealth
}

/// <summary>Inclusive calendar dates in the business's configured time zone.</summary>
public sealed record DateRange(DateOnly Start, DateOnly End);
public sealed record BusinessQuery(BusinessMetric Metric, DateRange Period, int Limit = 10,
    DateRange? ComparisonPeriod = null);

/// <summary>Created by a trusted authenticated host, never from a prompt or request JSON.</summary>
public sealed class BusinessSecurityContext
{
    public BusinessSecurityContext(string tenantId, IEnumerable<string> allowedBranchIds,
        string activeBranchId, string userId, IEnumerable<string> permissions,
        string correlationId, IEnumerable<string>? roles = null)
    {
        TenantId = tenantId;
        AllowedBranchIds = allowedBranchIds.ToFrozenSet(StringComparer.Ordinal);
        ActiveBranchId = activeBranchId;
        UserId = userId;
        Permissions = permissions.ToFrozenSet(StringComparer.Ordinal);
        CorrelationId = correlationId;
        Roles = (roles ?? []).ToFrozenSet(StringComparer.Ordinal);
    }
    public string TenantId { get; }
    public IReadOnlySet<string> AllowedBranchIds { get; }
    public string ActiveBranchId { get; }
    public string UserId { get; }
    public IReadOnlySet<string> Permissions { get; }
    public string CorrelationId { get; }
    public IReadOnlySet<string> Roles { get; }
    public BusinessScope Scope => new(TenantId, ActiveBranchId, UserId);
}

public sealed record BusinessScope(string TenantId, string BranchId, string UserId);
public sealed record BusinessMeasure(string Name, decimal Value, string Unit);
public sealed record BusinessRow(string Label, IReadOnlyList<BusinessMeasure> Measures);
public sealed record BusinessDataResult(BusinessMetric Metric, BusinessScope Scope,
    DateRange Period, IReadOnlyList<BusinessMeasure> Measures, IReadOnlyList<BusinessRow> Rows,
    string Source, DateTimeOffset GeneratedAt, string? CapabilityMessage = null);

public interface IBusinessDataProvider
{
    IReadOnlySet<BusinessMetric> SupportedMetrics { get; }
    Task<BusinessDataResult> ExecuteAsync(BusinessQuery query, BusinessSecurityContext security,
        CancellationToken cancellationToken = default);
}

public sealed record BusinessTool(BusinessMetric Metric, string Permission, string Description);
public sealed record BusinessAnswer(BusinessDataResult Facts, string DisplayText, string? Narrative,
    string ProviderStatus, string CorrelationId, AIUsage? Usage = null);

public sealed record AIAuditEvent(DateTimeOffset Timestamp, string CorrelationId,
    BusinessScope Scope, BusinessMetric? Metric, string? ProviderId, string? ModelId,
    long DurationMilliseconds, string Outcome, AIUsage? Usage = null);
public interface IAIAuditSink
{
    Task WriteAsync(AIAuditEvent auditEvent, CancellationToken cancellationToken = default);
}

public sealed class BusinessAccessException(string message) : Exception(message);
public sealed class BusinessQueryException(string message) : Exception(message);
public sealed class BusinessCapabilityException(string message) : Exception(message);
