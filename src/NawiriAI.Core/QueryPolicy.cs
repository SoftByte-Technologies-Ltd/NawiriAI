using NawiriAI.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NawiriAI.Core;

public sealed record IntelligenceLimits(int MaxRows = 100, int MaxPeriodDays = 366,
    int QueryTimeoutSeconds = 15, int MaxProviderPayloadCharacters = 16000);

public sealed class QueryPolicy(IntelligenceLimits? limits = null)
{
    private readonly IntelligenceLimits limits = limits ?? new();
    public void Validate(BusinessQuery query, BusinessSecurityContext context)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId) || string.IsNullOrWhiteSpace(context.UserId)
            || string.IsNullOrWhiteSpace(context.CorrelationId) || string.IsNullOrWhiteSpace(context.ActiveBranchId)
            || !context.AllowedBranchIds.Contains(context.ActiveBranchId))
            throw new BusinessAccessException("An authenticated company, branch and user scope is required.");
        if (!Enum.IsDefined(query.Metric)) throw new BusinessQueryException("Unknown business tool.");
        if (!context.Permissions.Contains(ToolRegistry.Permission(query.Metric)))
            throw new BusinessAccessException("You do not have permission to access this business data.");
        if (query.Metric is BusinessMetric.DailyBrief or BusinessMetric.BusinessHealth &&
            new[] { "sales.read", "inventory.read", "customers.read", "expenses.read" }.Any(p => !context.Permissions.Contains(p)))
            throw new BusinessAccessException("Business briefs require permission for each included data category.");
        if (query.Limit < 1 || query.Limit > limits.MaxRows) throw new BusinessQueryException("Requested row limit is outside the configured bounds.");
        ValidatePeriod(query.Period);
        if (query.Metric == BusinessMetric.PeriodComparison && query.ComparisonPeriod is null)
            throw new BusinessQueryException("A comparison period is required.");
        if (query.ComparisonPeriod is not null)
        {
            if (query.Metric != BusinessMetric.PeriodComparison) throw new BusinessQueryException("Comparison period is only valid for sales comparisons.");
            ValidatePeriod(query.ComparisonPeriod);
        }
    }

    private void ValidatePeriod(DateRange? period)
    {
        if (period is null || period.Start == default || period.End < period.Start || period.End.DayNumber - period.Start.DayNumber + 1 > limits.MaxPeriodDays)
            throw new BusinessQueryException("Invalid date range or report period exceeds configured bounds.");
    }

    public static BusinessQuery ParseJson(string json)
    {
        if (json.Length > 4000) throw new BusinessQueryException("Structured query is too large.");
        try
        {
            using var document = JsonDocument.Parse(json, new() { MaxDepth = 8 });
            RejectDuplicateProperties(document.RootElement);
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
            options.Converters.Add(new JsonStringEnumConverter<BusinessMetric>(allowIntegerValues: false));
            if (!document.RootElement.TryGetProperty("metric", out var metric) || metric.ValueKind != JsonValueKind.String
                || !Enum.GetNames<BusinessMetric>().Contains(metric.GetString(), StringComparer.Ordinal)
                || !document.RootElement.TryGetProperty("period", out _))
                throw new JsonException();
            return JsonSerializer.Deserialize<BusinessQuery>(json, options) ?? throw new JsonException();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        { throw new BusinessQueryException("Invalid structured query. Only metric, period, limit and comparisonPeriod are accepted; scope comes from authentication."); }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new JsonException();
            RejectDuplicateProperties(property.Value);
        }
    }
}
