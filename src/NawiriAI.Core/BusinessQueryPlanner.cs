using NawiriAI.Abstractions;
using System.Text.RegularExpressions;
namespace NawiriAI.Core;
public sealed class BusinessQueryPlanner
{
    public BusinessQuery Plan(string question, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length > 2000)
            throw new BusinessQueryException("Enter a business question of at most 2000 characters.");
        var text = question.ToLowerInvariant().Replace('’', '\'');
        // Defense in depth only: scope and tool authorization are enforced independently by QueryPolicy.
        if (Regex.IsMatch(text, @"\b(branch(?:es)?|compan(?:y|ies)|tenant|user)\b") &&
            !Regex.IsMatch(text, @"\b(my|current|this) branch\b"))
            throw new BusinessAccessException("Only the current authenticated branch can be queried.");
        if (Regex.IsMatch(text, @"\b(ignore|override|bypass|password|secret|token|credentials?|sql|drop|delete|insert|update|truncate|alter|execute|reveal)\b|api[ -]?key"))
            throw new BusinessQueryException("Only approved read-only business questions are supported.");
        if (Regex.IsMatch(text, @"\b(19\d{2}|20\d{2}|year|quarter|january|february|march|april|may|june|july|august|september|october|november|december|tomorrow|next)\b"))
            throw new BusinessQueryException("Use today, yesterday, this week, this month or last month; use a structured query for explicit dates.");
        var (period, comparisonPeriod) = ResolvePeriods(text, today);
        var limitMatch = Regex.Match(text, @"\btop\s+(\d+)\b");
        var limit = limitMatch.Success && int.TryParse(limitMatch.Groups[1].Value, out var n) ? n : 10;
        if (limitMatch.Success && !int.TryParse(limitMatch.Groups[1].Value, out _))
            throw new BusinessQueryException("Requested row limit is too large.");
        BusinessMetric metric;
        if (text.Contains("top") && text.Contains("product") || text.Contains("top-selling")) metric = BusinessMetric.TopProducts;
        else if (text.Contains("slow") || text.Contains("not sold")) metric = BusinessMetric.SlowMovingProducts;
        else if (text.Contains("categor") && (text.Contains("profit") || text.Contains("margin"))) metric = BusinessMetric.CategoryProfitability;
        else if (text.Contains("categor")) metric = BusinessMetric.CategoryPerformance;
        else if (text.Contains("payment") || text.Contains("tender")) metric = BusinessMetric.PaymentMix;
        else if (text.Contains("owe") || text.Contains("receivable") || text.Contains("debtor")) metric = BusinessMetric.Receivables;
        else if (text.Contains("top") && text.Contains("customer")) metric = BusinessMetric.TopCustomers;
        else if (text.Contains("customer")) metric = BusinessMetric.CustomerSummary;
        else if (text.Contains("low stock") || text.Contains("run out")) metric = BusinessMetric.LowStock;
        else if (text.Contains("stock") || text.Contains("inventory")) metric = BusinessMetric.InventorySummary;
        else if (text.Contains("expense")) metric = BusinessMetric.ExpensesSummary;
        else if (text.Contains("discount")) metric = BusinessMetric.DiscountsAnalysis;
        else if (text.Contains("void") || text.Contains("return")) metric = BusinessMetric.VoidsReturns;
        else if (text.Contains("staff") || text.Contains("cashier")) metric = BusinessMetric.StaffPerformance;
        else if (text.Contains("gross") || text.Contains("profit") || text.Contains("margin")) metric = BusinessMetric.GrossMargin;
        else if (text.Contains("health")) metric = BusinessMetric.BusinessHealth;
        else if (text.Contains("brief") || text.Contains("summarize") || text.Contains("management")) metric = BusinessMetric.DailyBrief;
        else if (text.Contains("trend")) metric = BusinessMetric.SalesTrend;
        else if (text.Contains("sale") || Regex.IsMatch(text, @"\b(sell|sold)\b")) metric = BusinessMetric.SalesSummary;
        else if (Regex.IsMatch(text, @"^\s*compare\s+(this month|this week|today)\s+(with|to|vs\.?)\s+(last month|last week|yesterday)[.?!]?\s*$")) metric = BusinessMetric.SalesSummary;
        else throw new BusinessCapabilityException("This question is not supported. Try sales, top products, or a sales comparison.");
        if (comparisonPeriod is not null)
        {
            if (metric != BusinessMetric.SalesSummary)
                throw new BusinessQueryException("Only sales comparisons are supported. Query the other metric for one period at a time.");
            metric = BusinessMetric.PeriodComparison;
        }
        return new(metric, period, limit, metric == BusinessMetric.PeriodComparison ? comparisonPeriod : null);
    }

    private static (DateRange Period, DateRange? Comparison) ResolvePeriods(string text, DateOnly today)
    {
        const string supportedPeriods = @"\b(today|yesterday|this week|last week|this month|last month)\b";
        var named = Regex.Matches(text, supportedPeriods).Select(match => match.Value).Distinct().ToArray();
        var remaining = Regex.Replace(text, supportedPeriods, "");
        if (Regex.IsMatch(remaining, @"\b(last|past|previous|days?|weeks?|months?|between|since|until|before|after|excluding|except|only|from)\b|\d{1,2}[-/]\d{1,2}"))
            throw new BusinessQueryException("That period or filter is not supported. Use an explicit structured query or a supported calendar period.");
        var month = new DateOnly(today.Year, today.Month, 1);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var periods = new Dictionary<string, DateRange>
        {
            ["today"] = new(today, today),
            ["yesterday"] = new(today.AddDays(-1), today.AddDays(-1)),
            ["this week"] = new(monday, today),
            ["last week"] = new(monday.AddDays(-7), monday.AddDays(-1)),
            ["this month"] = new(month, today),
            ["last month"] = new(month.AddMonths(-1), month.AddDays(-1))
        };
        if (text.Contains("compare"))
        {
            foreach (var (current, previous) in new[] { ("today", "yesterday"), ("this week", "last week"), ("this month", "last month") })
                if (named.Length == 2 && named.Contains(current) && named.Contains(previous)) return (periods[current], periods[previous]);
            throw new BusinessQueryException("Compare today with yesterday, this week with last week, or this month with last month.");
        }
        if (named.Length > 1) throw new BusinessQueryException("Specify one period or an explicit comparison.");
        return (periods[named.Length == 0 ? "this month" : named[0]], null);
    }
}
