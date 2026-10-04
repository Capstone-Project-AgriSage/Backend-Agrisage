using AgriSage.Application.Features.Reports;

namespace AgriSage.UnitTests.Application.Reports;

// Request validation of the sales report (FLOW_1 section 10), before any database access.
public class SalesReportValidatorTests
{
    private static readonly DateOnly Start = new(2026, 10, 1);

    private static bool Valid(SalesReportRequest request) => new SalesReportRequestValidator().Validate(request).IsValid;

    [Fact]
    public void A_period_has_both_days_in_order()
    {
        Assert.True(Valid(new SalesReportRequest { FromDate = Start, ToDate = Start }));
        Assert.True(Valid(new SalesReportRequest { FromDate = Start, ToDate = Start.AddDays(30) }));
        Assert.False(Valid(new SalesReportRequest { ToDate = Start }));
        Assert.False(Valid(new SalesReportRequest { FromDate = Start }));
        Assert.False(Valid(new SalesReportRequest()));
        Assert.False(Valid(new SalesReportRequest { FromDate = Start, ToDate = Start.AddDays(-1) }));
    }

    [Fact]
    public void A_period_is_at_most_366_days_both_ends_included()
    {
        Assert.True(Valid(new SalesReportRequest { FromDate = Start, ToDate = Start.AddDays(SalesReportRequestValidator.MaxDays - 1) }));
        Assert.False(Valid(new SalesReportRequest { FromDate = Start, ToDate = Start.AddDays(SalesReportRequestValidator.MaxDays) }));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("DAY", true)]
    [InlineData("product", true)]
    [InlineData(" staff ", true)]
    [InlineData("CUSTOMER_GROUP", true)]
    [InlineData("MONTH", false)]
    [InlineData("CUSTOMERGROUP", false)]
    public void Grouping_is_one_of_the_four_kinds(string? groupBy, bool valid) =>
        Assert.Equal(valid, Valid(new SalesReportRequest { FromDate = Start, ToDate = Start, GroupBy = groupBy }));
}
