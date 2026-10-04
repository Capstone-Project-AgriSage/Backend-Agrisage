using AgriSage.Application.Features.Customers;
using AgriSage.Application.Features.Credit;

namespace AgriSage.UnitTests.Application.Customers;

public class CustomerValidatorTests
{
    private static CreateCustomerRequest Valid => new() { FullName = "Nguyen Van A", PhoneNumber = "0901234567", Password = "long-enough-password" };

    [Fact]
    public void Create_accepts_phone_or_email_without_requiring_both()
    {
        var validator = new CreateCustomerRequestValidator();
        Assert.True(validator.Validate(Valid).IsValid);
        Assert.True(validator.Validate(Valid with { PhoneNumber = null, Email = "farmer@example.test" }).IsValid);
    }

    [Fact]
    public void Walk_in_cannot_be_created_as_a_registered_profile() =>
        Assert.False(new CreateCustomerRequestValidator().Validate(Valid with { CustomerType = "WALK_IN" }).IsValid);

    [Fact]
    public void Required_name_contact_and_password_are_checked()
    {
        var v = new CreateCustomerRequestValidator();
        Assert.False(v.Validate(Valid with { FullName = " " }).IsValid);
        Assert.False(v.Validate(Valid with { PhoneNumber = null }).IsValid);
        Assert.False(v.Validate(Valid with { Password = "short" }).IsValid);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("09012345678")]
    [InlineData("abc")]
    public void Invalid_phone_is_rejected(string phone) =>
        Assert.False(new CreateCustomerRequestValidator().Validate(Valid with { PhoneNumber = phone }).IsValid);

    [Theory]
    [InlineData(-1)]
    [InlineData(1.001)]
    public void Invalid_credit_money_is_rejected(decimal limit) =>
        Assert.False(new UpdateCustomerRequestValidator().Validate(new UpdateCustomerRequest { FullName = "A", Email = "a@example.test", CreditLimit = limit }).IsValid);

    [Fact]
    public void Disabling_credit_does_not_accept_a_limit_to_overwrite_financial_policy() =>
        Assert.False(new CreateCustomerRequestValidator().Validate(Valid with { AllowCreditPurchase = false, CreditLimit = 0 }).IsValid);

    [Fact]
    public void Paging_sort_and_enum_filters_are_validated()
    {
        var v = new CustomerListRequestValidator();
        Assert.True(v.Validate(new CustomerListRequest { Status = "active", SortBy = "CURRENT_DEBT", CustomerType = "WALK_IN" }).IsValid);
        Assert.False(v.Validate(new CustomerListRequest { Page = 0 }).IsValid);
        Assert.False(v.Validate(new CustomerListRequest { PageSize = 101 }).IsValid);
        Assert.False(v.Validate(new CustomerListRequest { SortBy = "password" }).IsValid);
        Assert.False(v.Validate(new CustomerListRequest { Status = "123" }).IsValid);
    }

    [Fact]
    public void Order_history_rejects_invalid_status_and_reversed_or_overflowing_dates()
    {
        var v = new CustomerOrderListRequestValidator();
        Assert.False(v.Validate(new CustomerOrderListRequest { PaymentStatus = "FAILED" }).IsValid);
        Assert.False(v.Validate(new CustomerOrderListRequest { FromDate = new(2026, 10, 4), ToDate = new(2026, 10, 3) }).IsValid);
        Assert.False(v.Validate(new CustomerOrderListRequest { ToDate = DateOnly.MaxValue }).IsValid);
        Assert.True(v.Validate(new CustomerOrderListRequest { Status = "PARTIALLY_FULFILLED", PaymentStatus = "PARTIALLY_PAID" }).IsValid);
    }

    [Fact]
    public void Credit_changes_need_reason_and_nonnegative_term()
    {
        Assert.False(new CustomerCreditLimitRequestValidator().Validate(new CustomerCreditLimitRequest(100, " ")).IsValid);
        Assert.False(new CreditTierRequestValidator().Validate(new CreditTierRequest("REGULAR", "Regular", 100, -1)).IsValid);
        Assert.True(new CustomerCreditLimitRequestValidator().Validate(new CustomerCreditLimitRequest(0, "Reduce exposure")).IsValid);
    }

    [Fact]
    public void Address_and_group_ids_are_validated()
    {
        Assert.False(new CreateCustomerRequestValidator().Validate(Valid with { CustomerGroupId = Guid.Empty }).IsValid);
        Assert.False(new CreateCustomerRequestValidator().Validate(Valid with { Address = new("A", "123", "", "") }).IsValid);
        Assert.False(new AssignCustomerGroupRequestValidator().Validate(new AssignCustomerGroupRequest(Guid.Empty)).IsValid);
    }
}

