using AgriSage.Application.Features.Credit;
using AgriSage.Application.Features.Debt;
using AgriSage.Application.Features.Payments;
using AgriSage.Application.Features.Reports;

namespace AgriSage.UnitTests.Application.Credit;

public sealed class CreditDebtValidatorTests
{
    [Theory]
    [InlineData(-1, false)] [InlineData(0, true)] [InlineData(1.001, false)] [InlineData(20000000, true)]
    public void Eligibility_money_is_non_negative_and_exact(decimal amount, bool valid) =>
        Assert.Equal(valid, new CreditEligibilityRequestValidator().Validate(new CreditEligibilityRequest(amount)).IsValid);
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(1.001)]
    public void Bank_and_adjustment_require_positive_money(decimal amount)
    {
        Assert.False(new BankDebtPaymentRequestValidator().Validate(new BankDebtPaymentRequest(Guid.NewGuid(), amount)).IsValid);
        Assert.False(new DebtAdjustmentRequestValidator().Validate(new DebtAdjustmentRequest(amount, "reason")).IsValid);
    }
    [Fact]
    public void Rejection_and_adjustment_require_a_reason()
    {
        Assert.False(new RejectDebtPaymentRequestValidator().Validate(new RejectDebtPaymentRequest(" ")).IsValid);
        Assert.False(new DebtAdjustmentRequestValidator().Validate(new DebtAdjustmentRequest(1, "")).IsValid);
    }
    [Fact]
    public void Lists_reject_invalid_status_sort_and_dates()
    {
        var v = new DebtEntryListRequestValidator();
        Assert.False(v.Validate(new DebtEntryListRequest { Status = "SETTLED" }).IsValid);
        Assert.False(v.Validate(new DebtEntryListRequest { SortBy = "arbitrary" }).IsValid);
        Assert.False(v.Validate(new DebtEntryListRequest { PageSize = 0 }).IsValid);
        Assert.False(v.Validate(new DebtEntryListRequest { DueFrom = new(2026, 10, 4), DueTo = new(2026, 10, 3) }).IsValid);
        Assert.True(v.Validate(new DebtEntryListRequest { Status = "PAID", SortBy = "DaysOverdue" }).IsValid);
    }
    [Fact]
    public void Collection_reports_reject_inverted_or_excessive_periods()
    {
        var v = new DebtCollectionRequestValidator();
        Assert.False(v.Validate(new DebtCollectionRequest { FromDate = new(2026, 10, 4), ToDate = new(2026, 10, 3) }).IsValid);
        Assert.False(v.Validate(new DebtCollectionRequest { FromDate = new(2025, 1, 1), ToDate = new(2026, 10, 3) }).IsValid);
        Assert.True(v.Validate(new DebtCollectionRequest { FromDate = new(2026, 10, 1), ToDate = new(2026, 10, 4), GroupBy = "METHOD" }).IsValid);
    }
}
