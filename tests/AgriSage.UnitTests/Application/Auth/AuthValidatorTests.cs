using AgriSage.Application.Features.Auth;
using AgriSage.Application.Features.Auth.Dtos.Requests;
using AgriSage.Application.Features.Auth.Validators;
using AgriSage.Domain.Features.Identity.Enums;

namespace AgriSage.UnitTests.Application.Auth;

public class AuthValidatorTests
{
    private readonly RegisterFarmerRequestValidator _register = new();
    private readonly LoginRequestValidator _login = new();
    private readonly CreateAdminRequestValidator _admin = new();

    private static RegisterFarmerRequest Register(
        string fullName = "Nguyen Van A",
        string? phone = "0912345678",
        string? email = null,
        string password = "password1") => new(fullName, phone, email, password);

    [Fact]
    public void Register_with_a_phone_only_is_valid() => Assert.True(_register.Validate(Register()).IsValid);

    [Fact]
    public void Register_with_an_email_only_is_valid() =>
        Assert.True(_register.Validate(Register(phone: null, email: "a@b.vn")).IsValid);

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", "  ")]
    public void Register_requires_a_phone_or_an_email(string? phone, string? email)
    {
        var result = _register.Validate(Register(phone: phone, email: email));

        Assert.Contains(result.Errors, error => error.PropertyName == "contact");
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("0112345678")]
    [InlineData("+1 415 555 2671")]
    public void Register_rejects_an_invalid_phone(string phone) =>
        Assert.Contains(_register.Validate(Register(phone: phone)).Errors, error => error.PropertyName == "PhoneNumber");

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@")]
    public void Register_rejects_an_invalid_email(string email) =>
        Assert.Contains(_register.Validate(Register(phone: null, email: email)).Errors, error => error.PropertyName == "Email");

    [Theory]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void Register_password_length_is_8_to_128(int length, bool valid) =>
        Assert.Equal(valid, _register.Validate(Register(password: new string('a', length))).IsValid);

    [Fact]
    public void Register_requires_a_full_name_of_at_most_150_characters()
    {
        Assert.False(_register.Validate(Register(fullName: " ")).IsValid);
        Assert.False(_register.Validate(Register(fullName: new string('a', 151))).IsValid);
        Assert.True(_register.Validate(Register(fullName: new string('a', 150))).IsValid);
    }

    [Fact]
    public void Login_does_not_enforce_the_password_policy() =>
        Assert.True(_login.Validate(new LoginRequest("0912345678", "x")).IsValid);

    [Theory]
    [InlineData("", "password")]
    [InlineData("0912345678", "")]
    public void Login_requires_both_fields(string identifier, string password) =>
        Assert.False(_login.Validate(new LoginRequest(identifier, password)).IsValid);

    [Fact]
    public void Admin_requires_a_valid_email_and_password()
    {
        Assert.True(_admin.Validate(new CreateAdminRequest("Administrator", "admin@example.com", null, "password1")).IsValid);
        Assert.False(_admin.Validate(new CreateAdminRequest("Administrator", "", null, "password1")).IsValid);
        Assert.False(_admin.Validate(new CreateAdminRequest("Administrator", "admin@example.com", null, "short")).IsValid);
        Assert.False(_admin.Validate(new CreateAdminRequest("Administrator", "admin@example.com", "123", "password1")).IsValid);
    }

    [Theory]
    [InlineData(RoleCode.Farmer, "FARMER")]
    [InlineData(RoleCode.StoreOwner, "STORE_OWNER")]
    [InlineData(RoleCode.SalesStaff, "SALES_STAFF")]
    [InlineData(RoleCode.DeliveryStaff, "DELIVERY_STAFF")]
    [InlineData(RoleCode.Admin, "ADMIN")]
    public void Role_code_text_matches_the_database_values(RoleCode role, string expected) =>
        Assert.Equal(expected, RoleCodeFormat.ToText(role));
}
