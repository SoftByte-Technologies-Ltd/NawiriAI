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
        var month = new DateOnly(today.Year, today.Month, 1);
        var previous = new DateRange(month.AddMonths(-1), month.AddDays(-1));
        var period = text.Contains("yesterday") ? new DateRange(today.AddDays(-1), today.AddDays(-1))
            : text.Contains("today") ? new DateRange(today, today)
            : text.Contains("this week") ? new DateRange(today.AddDays(-(((int)today.DayOfWeek + 6) % 7)), today)
            : text.Contains("last week") ? new DateRange(today.AddDays(-(((int)today.DayOfWeek + 6) % 7) - 7), today.AddDays(-(((int)today.DayOfWeek + 6) % 7) - 1))
            : text.Contains("last month") && !text.Contains("compare") ? previous
            : new DateRange(month, today);
        var limitMatch = Regex.Match(text, @"\btop\s+(\d+)\b");
        var limit = limitMatch.Success && int.TryParse(limitMatch.Groups[1].Value, out var n) ? n : 10;
        if (limitMatch.Success && !int.TryParse(limitMatch.Groups[1].Value, out _))
            throw new BusinessQueryException("Requested row limit is too large.");
        BusinessMetric metric;
        if (text.Contains("compare") && (text.Contains("sale") || text.Contains("month"))) metric = BusinessMetric.PeriodComparison;
        else if (text.Contains("top") && text.Contains("product") || text.Contains("top-selling")) metric = BusinessMetric.TopProducts;
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
        else throw new BusinessCapabilityException("This question is not supported. Try sales, top products, or a sales comparison.");
        return new(metric, period, limit, metric == BusinessMetric.PeriodComparison ? previous : null);
    }
}
