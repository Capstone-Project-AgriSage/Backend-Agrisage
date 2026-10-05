using AgriSage.Application.Common.Interfaces;
using AgriSage.Application.Features.Customers;

namespace AgriSage.UnitTests.Application.Customers;

public class FarmerProfileValidatorTests
{
    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => new(2026, 10, 4, 3, 0, 0, TimeSpan.Zero);
    }

    private static readonly AddressRequest Address = new("Nguyen Van A", "0901234567", "Ap 3, xa Tan Phu", "Can Tho", "FARM");

    [Theory]
    [InlineData("HOME")]
    [InlineData("farm")]
    [InlineData("Other")]
    public void Address_types_are_accepted_case_insensitively(string type) =>
        Assert.True(new AddressRequestValidator().Validate(Address with { AddressType = type }).IsValid);

    [Theory]
    [InlineData("OFFICE")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_or_missing_address_type_is_rejected(string? type) =>
        Assert.False(new AddressRequestValidator().Validate(Address with { AddressType = type! }).IsValid);

    [Fact]
    public void Required_address_fields_are_checked()
    {
        var validator = new AddressRequestValidator();
        Assert.False(validator.Validate(Address with { RecipientName = " " }).IsValid);
        Assert.False(validator.Validate(Address with { AddressLine = "" }).IsValid);
        Assert.False(validator.Validate(Address with { Province = "" }).IsValid);
        Assert.False(validator.Validate(Address with { RecipientPhone = "123" }).IsValid);
        Assert.True(validator.Validate(Address with { RecipientPhone = "+84 901 234 567" }).IsValid);
    }

    [Fact]
    public void Lengths_follow_the_user_addresses_columns()
    {
        var validator = new AddressRequestValidator();
        Assert.False(validator.Validate(Address with { RecipientName = new string('a', 151) }).IsValid);
        Assert.False(validator.Validate(Address with { AddressLine = new string('a', 501) }).IsValid);
        Assert.False(validator.Validate(Address with { Ward = new string('a', 151) }).IsValid);
        Assert.True(validator.Validate(Address with { AddressLine = new string('a', 500) }).IsValid);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    [InlineData(10.12345678, 0)]
    public void Coordinates_must_fit_numeric_10_7(double latitude, double longitude) =>
        Assert.False(new AddressRequestValidator()
            .Validate(Address with { Latitude = (decimal)latitude, Longitude = (decimal)longitude }).IsValid);

    [Fact]
    public void Valid_coordinates_are_accepted() =>
        Assert.True(new AddressRequestValidator().Validate(Address with { Latitude = 10.0452m, Longitude = 105.7469123m }).IsValid);

    [Theory]
    [InlineData("MALE")]
    [InlineData("female")]
    [InlineData("OTHER")]
    [InlineData(null)]
    public void Gender_is_male_female_other_or_null(string? gender) =>
        Assert.True(new UpdateMyProfileRequestValidator(new FixedClock()).Validate(new UpdateMyProfileRequest("A", null, gender)).IsValid);

    [Fact]
    public void Profile_rejects_unknown_gender_blank_name_and_impossible_birth_dates()
    {
        var validator = new UpdateMyProfileRequestValidator(new FixedClock());
        Assert.False(validator.Validate(new UpdateMyProfileRequest("A", null, "X")).IsValid);
        Assert.False(validator.Validate(new UpdateMyProfileRequest(" ")).IsValid);
        Assert.False(validator.Validate(new UpdateMyProfileRequest("A", new DateOnly(2026, 10, 5))).IsValid);
        Assert.False(validator.Validate(new UpdateMyProfileRequest("A", new DateOnly(1899, 12, 31))).IsValid);
        Assert.True(validator.Validate(new UpdateMyProfileRequest("A", new DateOnly(1980, 5, 1))).IsValid);
    }
}
