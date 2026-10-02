using AgriSage.Application.Features.Auth;
using AgriSage.Application.Features.Auth.Dtos.Requests;
using AgriSage.Application.Features.Auth.Validators;
using AgriSage.Application.Features.Staff;
using AgriSage.Application.Features.Staff.Dtos.Requests;
using AgriSage.Application.Features.Staff.Validators;
using AgriSage.Domain.Features.Identity.Enums;

namespace AgriSage.UnitTests.Application.Staff;

public class StaffPolicyAndValidatorTests
{
    [Theory]
    [InlineData(RoleCode.Admin, RoleCode.StoreOwner, true)]
    [InlineData(RoleCode.Admin, RoleCode.SalesStaff, true)]
    [InlineData(RoleCode.Admin, RoleCode.DeliveryStaff, true)]
    [InlineData(RoleCode.Admin, RoleCode.Admin, false)]
    [InlineData(RoleCode.Admin, RoleCode.Farmer, false)]
    [InlineData(RoleCode.StoreOwner, RoleCode.SalesStaff, true)]
    [InlineData(RoleCode.StoreOwner, RoleCode.DeliveryStaff, true)]
    [InlineData(RoleCode.StoreOwner, RoleCode.StoreOwner, false)]
    [InlineData(RoleCode.StoreOwner, RoleCode.Admin, false)]
    [InlineData(RoleCode.StoreOwner, RoleCode.Farmer, false)]
    [InlineData(RoleCode.SalesStaff, RoleCode.SalesStaff, false)]
    [InlineData(RoleCode.DeliveryStaff, RoleCode.DeliveryStaff, false)]
    [InlineData(RoleCode.Farmer, RoleCode.SalesStaff, false)]
    public void Management_matrix(RoleCode actor, RoleCode target, bool allowed) =>
        Assert.Equal(allowed, StaffPolicy.CanManage(actor, target));

    [Theory]
    [InlineData(RoleCode.Admin, true)]
    [InlineData(RoleCode.StoreOwner, true)]
    [InlineData(RoleCode.SalesStaff, false)]
    [InlineData(RoleCode.DeliveryStaff, false)]
    [InlineData(RoleCode.Farmer, false)]
    public void Only_admin_and_store_owner_use_the_staff_api(RoleCode actor, bool allowed) =>
        Assert.Equal(allowed, StaffPolicy.CanUseStaffApi(actor));

    [Fact]
    public void Staff_roles_are_exactly_owner_sales_and_delivery() =>
        Assert.Equal([RoleCode.StoreOwner, RoleCode.SalesStaff, RoleCode.DeliveryStaff], StaffPolicy.StaffRoles);

    [Theory]
    [InlineData("STORE_OWNER", true, RoleCode.StoreOwner)]
    [InlineData("sales_staff", true, RoleCode.SalesStaff)]
    [InlineData(" DELIVERY_STAFF ", true, RoleCode.DeliveryStaff)]
    [InlineData("ADMIN", true, RoleCode.Admin)]
    [InlineData("FARMER", true, RoleCode.Farmer)]
    [InlineData("MANAGER", false, default(RoleCode))]
    [InlineData("", false, default(RoleCode))]
    [InlineData(null, false, default(RoleCode))]
    public void Role_text_parses_back_to_the_role(string? text, bool parsed, RoleCode expected)
    {
        Assert.Equal(parsed, RoleCodeFormat.TryParse(text, out var role));
        Assert.Equal(expected, role);
    }

    private static CreateStaffRequest Create(
        string role = "SALES_STAFF",
        string? phone = "0912345678",
        string? email = null,
        string password = "password1") => new("Nguyen Van B", role, phone, email, password);

    [Fact]
    public void Create_with_valid_data_is_valid() => Assert.True(new CreateStaffRequestValidator().Validate(Create()).IsValid);

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("FARMER")]
    [InlineData("MANAGER")]
    [InlineData("")]
    public void Create_rejects_a_role_that_is_not_staff(string role) =>
        Assert.Contains(new CreateStaffRequestValidator().Validate(Create(role: role)).Errors, e => e.PropertyName == "Role");

    [Fact]
    public void Create_requires_a_phone_or_email_and_a_valid_password()
    {
        var result = new CreateStaffRequestValidator().Validate(Create(phone: null, email: null, password: "short"));

        Assert.Contains(result.Errors, e => e.PropertyName == "contact");
        Assert.Contains(result.Errors, e => e.PropertyName == "Password");
    }

    [Fact]
    public void Update_requires_a_name_and_a_contact()
    {
        var validator = new UpdateStaffRequestValidator();

        Assert.True(validator.Validate(new UpdateStaffRequest("A", "0912345678", null)).IsValid);
        Assert.False(validator.Validate(new UpdateStaffRequest("", "0912345678", null)).IsValid);
        Assert.False(validator.Validate(new UpdateStaffRequest("A", null, null)).IsValid);
        Assert.False(validator.Validate(new UpdateStaffRequest("A", "123", null)).IsValid);
    }

    [Fact]
    public void List_request_validates_paging_role_and_status()
    {
        var validator = new StaffListRequestValidator();

        Assert.True(validator.Validate(new StaffListRequest()).IsValid);
        Assert.True(validator.Validate(new StaffListRequest { Role = "SALES_STAFF", Status = "locked", Search = "an" }).IsValid);
        Assert.False(validator.Validate(new StaffListRequest { PageSize = 0 }).IsValid);
        Assert.False(validator.Validate(new StaffListRequest { PageSize = 101 }).IsValid);
        Assert.False(validator.Validate(new StaffListRequest { Page = 0 }).IsValid);
        Assert.False(validator.Validate(new StaffListRequest { Role = "FARMER" }).IsValid);
        Assert.False(validator.Validate(new StaffListRequest { Status = "BOGUS" }).IsValid);
        Assert.False(validator.Validate(new StaffListRequest { Search = new string('a', 101) }).IsValid);
    }

    [Fact]
    public void Password_requests_enforce_the_length_policy()
    {
        Assert.False(new ResetStaffPasswordRequestValidator().Validate(new ResetStaffPasswordRequest("short")).IsValid);
        Assert.True(new ResetStaffPasswordRequestValidator().Validate(new ResetStaffPasswordRequest("password1")).IsValid);
        Assert.False(new ChangePasswordRequestValidator().Validate(new ChangePasswordRequest("old", "short")).IsValid);
        Assert.False(new ChangePasswordRequestValidator().Validate(new ChangePasswordRequest("", "password1")).IsValid);
        Assert.True(new ChangePasswordRequestValidator().Validate(new ChangePasswordRequest("old", "password1")).IsValid);
    }
}
