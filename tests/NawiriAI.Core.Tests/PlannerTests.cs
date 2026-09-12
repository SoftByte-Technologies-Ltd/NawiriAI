using NawiriAI.Abstractions;
using NawiriAI.Core;

namespace NawiriAI.Core.Tests;

public class PlannerTests
{
    [Theory]
    [InlineData("What were my sales this month?", BusinessMetric.SalesSummary, 1, 12)]
    [InlineData("What were sales today?", BusinessMetric.SalesSummary, 12, 12)]
    [InlineData("What were sales yesterday?", BusinessMetric.SalesSummary, 11, 11)]
    [InlineData("What are my top 10 products this week?", BusinessMetric.TopProducts, 7, 12)]
    public void ResolvesBusinessCalendar(string question, BusinessMetric metric, int start, int end)
    {
        var query = new BusinessQueryPlanner().Plan(question, new(2026, 9, 12));
        Assert.Equal(metric, query.Metric);
        Assert.Equal(new DateRange(new(2026, 9, start), new(2026, 9, end)), query.Period);
    }

    [Fact]
    public void ComparisonUsesFullPreviousMonthAndStatesBothPeriods()
    {
        var query = new BusinessQueryPlanner().Plan("Compare sales this month with last month.", new(2026, 3, 1));
        Assert.Equal(BusinessMetric.PeriodComparison, query.Metric);
        Assert.Equal(new DateRange(new(2026, 3, 1), new(2026, 3, 1)), query.Period);
        Assert.Equal(new DateRange(new(2026, 2, 1), new(2026, 2, 28)), query.ComparisonPeriod);
    }

    [Fact]
    public void ExplicitUnknownPeriodIsRejectedInsteadOfSilentlyUsingMonth()
    {
        Assert.Throws<BusinessQueryException>(() => new BusinessQueryPlanner().Plan("Sales in 2020", new(2026, 9, 12)));
    }

    [Theory]
    [InlineData("Compare sales today with yesterday", 12, 12, 11, 11)]
    [InlineData("Compare sales this week with last week", 7, 12, 1, 6)]
    public void ComparesTheRequestedPeriods(string question, int start, int end, int priorStart, int priorEnd)
    {
        var query = new BusinessQueryPlanner().Plan(question, new(2026, 9, 12));
        Assert.Equal(new DateRange(new(2026, 9, start), new(2026, 9, end)), query.Period);
        // The previous calendar week starts in August for this fixture.
        var expectedStart = question.Contains("week") ? new DateOnly(2026, 8, 31) : new(2026, 9, priorStart);
        Assert.Equal(new DateRange(expectedStart, new(2026, 9, priorEnd)), query.ComparisonPeriod);
    }

    [Theory]
    [InlineData("Sales in the last 7 days")]
    [InlineData("Sales in the past 7 days")]
    [InlineData("Sales today and last month")]
    [InlineData("Sales excluding refunds this month")]
    [InlineData("Compare sales")]
    public void UnsupportedOrAmbiguousDatesAndFiltersAreRejected(string question)
        => Assert.Throws<BusinessQueryException>(() => new BusinessQueryPlanner().Plan(question, new(2026, 9, 12)));
}
