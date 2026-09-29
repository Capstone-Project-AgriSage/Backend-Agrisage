using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Identity.Entities;

namespace AgriSage.UnitTests.Domain.Features.Identity;

public class UserTests
{
    private static readonly Guid RoleId = Guid.NewGuid();

    [Theory]
    [InlineData(null, null)]
    [InlineData("", " ")]
    public void User_requires_email_or_phone(string? email, string? phoneNumber)
    {
        Assert.Throws<DomainException>(() => new User(RoleId, "Nguyen Van A", "hash", email, phoneNumber));
    }

    [Theory]
    [InlineData("a@example.com", null)]
    [InlineData(null, "0900000000")]
    public void User_can_be_created_with_only_one_contact(string? email, string? phoneNumber)
    {
        var user = new User(RoleId, "Nguyen Van A", "hash", email, phoneNumber);

        Assert.Equal(email, user.Email);
        Assert.Equal(phoneNumber, user.PhoneNumber);
    }

    [Fact]
    public void UpdateContact_removing_both_contacts_throws_and_keeps_existing_values()
    {
        var user = new User(RoleId, "Nguyen Van A", "hash", "a@example.com", "0900000000");

        Assert.Throws<DomainException>(() => user.UpdateContact(null, null));

        Assert.Equal("a@example.com", user.Email);
        Assert.Equal("0900000000", user.PhoneNumber);
    }

    [Fact]
    public void Cannot_verify_missing_email_or_phone()
    {
        var phoneOnly = new User(RoleId, "Nguyen Van A", "hash", null, "0900000000");
        var emailOnly = new User(RoleId, "Nguyen Van B", "hash", "b@example.com", null);

        Assert.Throws<DomainException>(phoneOnly.MarkEmailVerified);
        Assert.Throws<DomainException>(emailOnly.MarkPhoneVerified);
    }
}
