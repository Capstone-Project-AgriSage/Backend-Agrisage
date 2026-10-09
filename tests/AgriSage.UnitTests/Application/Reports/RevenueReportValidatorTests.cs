using AgriSage.Application.Features.Reports;

namespace AgriSage.UnitTests.Application.Reports;

public class RevenueReportValidatorTests
{
    private static readonly RevenueReportRequest Valid = new() { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 31) };
    [Fact]
    public void Periods_are_required_ordered_bounded_and_safe_for_exclusive_end()
    {
        var validator = new RevenueReportRequestValidator();
        Assert.True(validator.Validate(Valid).IsValid);
        Assert.False(validator.Validate(Valid with { FromDate = null }).IsValid);
        Assert.False(validator.Validate(Valid with { ToDate = null }).IsValid);
        Assert.False(validator.Validate(Valid with { ToDate = Valid.FromDate!.Value.AddDays(-1) }).IsValid);
        Assert.True(validator.Validate(Valid with { ToDate = Valid.FromDate!.Value.AddDays(365) }).IsValid);
        Assert.False(validator.Validate(Valid with { ToDate = Valid.FromDate!.Value.AddDays(366) }).IsValid);
        Assert.False(validator.Validate(Valid with { FromDate = DateOnly.MaxValue, ToDate = DateOnly.MaxValue }).IsValid);
        Assert.False(validator.Validate(Valid with { FromDate = DateOnly.MinValue, ToDate = DateOnly.MinValue }).IsValid);
    }
    [Fact]
    public void All_revenue_dimensions_are_supported_but_unknown_values_and_unbounded_pages_are_rejected()
    {
        var validator = new RevenueReportRequestValidator();
        foreach (var by in RevenueReportRequestValidator.GroupBys)
            Assert.True(validator.Validate(Valid with { GroupBy = " " + by.ToLowerInvariant() + " " }).IsValid);
        Assert.False(validator.Validate(Valid with { GroupBy = "BOGUS" }).IsValid);
        Assert.False(validator.Validate(Valid with { GroupBy = "" }).IsValid);
        Assert.False(validator.Validate(Valid with { Page = 0 }).IsValid);
        Assert.False(validator.Validate(Valid with { PageSize = 101 }).IsValid);
        Assert.False(validator.Validate(Valid with { PageSize = 0 }).IsValid);
        Assert.True(validator.Validate(Valid with { Page = int.MaxValue, PageSize = 100 }).IsValid);
    }
    [Fact]
    public void Filters_use_defined_codes_and_real_identifiers()
    {
        var validator = new RevenueReportRequestValidator();
        Assert.True(validator.Validate(Valid with { Source = "farmer_web", SettlementType = "credit", StaffUserId = Guid.NewGuid() }).IsValid);
        Assert.False(validator.Validate(Valid with { Source = "0" }).IsValid);
        Assert.False(validator.Validate(Valid with { SettlementType = "INVALID" }).IsValid);
        Assert.False(validator.Validate(Valid with { FarmerProfileId = Guid.Empty }).IsValid);
        Assert.False(validator.Validate(Valid with { StoreProductId = Guid.Empty }).IsValid);
        Assert.False(validator.Validate(Valid with { StaffUserId = Guid.Empty }).IsValid);
        Assert.False(validator.Validate(Valid with { CategoryId = Guid.Empty }).IsValid);
        Assert.False(validator.Validate(Valid with { CustomerGroupId = Guid.Empty }).IsValid);
    }
    [Fact]
    public void Comparison_cannot_underflow_the_calendar()
    {
        var validator = new RevenueSummaryRequestValidator();
        Assert.True(validator.Validate(new RevenueSummaryRequest { FromDate = Valid.FromDate, ToDate = Valid.ToDate }).IsValid);
        Assert.False(validator.Validate(new RevenueSummaryRequest { FromDate = DateOnly.MinValue, ToDate = DateOnly.MinValue }).IsValid);
        Assert.False(validator.Validate(new RevenueSummaryRequest { FromDate = DateOnly.MinValue.AddDays(1), ToDate = DateOnly.MinValue.AddDays(1) }).IsValid);
    }
    [Fact]
    public void Operational_reports_have_distinct_grouping_contracts_and_the_same_period_bounds()
    {
        Assert.True(new PaymentReportRequestValidator().Validate(new PaymentReportRequest { FromDate = Valid.FromDate, ToDate = Valid.ToDate, GroupBy = " staff " }).IsValid);
        Assert.False(new PaymentReportRequestValidator().Validate(new PaymentReportRequest { FromDate = Valid.FromDate, ToDate = Valid.ToDate, GroupBy = "STATUS" }).IsValid);
        Assert.False(new OrderReportRequestValidator().Validate(new OrderReportRequest { FromDate = Valid.FromDate, ToDate = Valid.ToDate, GroupBy = "DAY" }).IsValid);
        Assert.False(new PurchaseReportRequestValidator().Validate(new PurchaseReportRequest { FromDate = Valid.FromDate }).IsValid);
        Assert.False(new ReturnReportRequestValidator().Validate(new ReturnReportRequest { FromDate = Valid.FromDate, ToDate = Valid.FromDate!.Value.AddDays(366) }).IsValid);
        Assert.False(new RefundReportRequestValidator().Validate(new RefundReportRequest { FromDate = Valid.FromDate, ToDate = DateOnly.MaxValue }).IsValid);
    }
}
