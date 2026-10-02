using AgriSage.Application.Common;

namespace AgriSage.UnitTests.Application.Auth;

public class ContactNormalizerTests
{
    [Theory]
    [InlineData("0912345678", "0912345678")]
    [InlineData("0912 345 678", "0912345678")]
    [InlineData("0912.345.678", "0912345678")]
    [InlineData("0912-345-678", "0912345678")]
    [InlineData("+84912345678", "0912345678")]
    [InlineData("+84 912 345 678", "0912345678")]
    [InlineData("84912345678", "0912345678")]
    [InlineData("  0356789012  ", "0356789012")]
    [InlineData("0501234567", "0501234567")]
    [InlineData("0701234567", "0701234567")]
    [InlineData("0801234567", "0801234567")]
    public void Valid_mobile_numbers_are_normalized_to_a_leading_zero(string input, string expected)
    {
        Assert.True(ContactNormalizer.TryNormalizePhone(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("091234567")]
    [InlineData("09123456789")]
    [InlineData("0112345678")]
    [InlineData("0212345678")]
    [InlineData("0412345678")]
    [InlineData("0612345678")]
    [InlineData("abcdefghij")]
    [InlineData("+1 415 555 2671")]
    [InlineData("+8412345")]
    public void Invalid_numbers_are_rejected(string? input)
    {
        Assert.False(ContactNormalizer.TryNormalizePhone(input, out var normalized));
        Assert.Null(normalized);
    }

    [Theory]
    [InlineData("  Farmer@Example.COM ", "farmer@example.com")]
    [InlineData("a@b.vn", "a@b.vn")]
    public void Email_is_trimmed_and_lower_cased(string input, string expected) =>
        Assert.Equal(expected, ContactNormalizer.NormalizeEmail(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Blank_email_becomes_null(string? input) => Assert.Null(ContactNormalizer.NormalizeEmail(input));
}
