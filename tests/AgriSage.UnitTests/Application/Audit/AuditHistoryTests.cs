using AgriSage.Application.Common;
using AgriSage.Application.Features.Audit;

namespace AgriSage.UnitTests.Application.Audit;

public sealed class AuditHistoryTests
{
    [Fact]
    public void Credentials_are_redacted_recursively_on_write_and_legacy_read_without_removing_business_values()
    {
        var values = new Dictionary<string, object?>
        {
            ["PasswordHash"] = "sensitive-test-value", ["access_token"] = "sensitive-test-value",
            ["sessionId"] = "safe-session-id", ["code"] = "SKU-001", ["quantity"] = 15,
            ["items"] = new[] { new { user = new { password = "sensitive-test-value", email = "safe@example.test" },
                refreshToken = "sensitive-test-value", OTP = "sensitive-test-value" } },
        };
        var written = AuditValues.Serialize(values)!;
        Assert.DoesNotContain("sensitive-test-value", written);
        Assert.Contains("safe@example.test", written);
        Assert.Contains("SKU-001", written);
        var legacy = AuditValues.Parse("{\"nested\":[{\"Api-Key\":\"sensitive-test-value\",\"quantity\":2}],\"SessionId\":\"safe\"}")!.Value;
        Assert.Equal("[REDACTED]", legacy.GetProperty("nested")[0].GetProperty("Api-Key").GetString());
        Assert.Equal(2, legacy.GetProperty("nested")[0].GetProperty("quantity").GetInt32());
        Assert.Equal("safe", legacy.GetProperty("SessionId").GetString());
        Assert.Null(AuditValues.Serialize(null));
        Assert.Null(AuditValues.Parse(null));
    }

    [Theory]
    [InlineData("OWNER", "SUCCESS", false)]
    [InlineData("STORE_OWNER", "SUCCESS", true)]
    [InlineData("sales_staff", "failure", true)]
    [InlineData("FARMER", "UNKNOWN", false)]
    [InlineData(null, null, true)]
    public void New_filters_accept_only_contract_values(string? role, string? status, bool valid)
    {
        Assert.Equal(valid, new AuditLogListRequestValidator().Validate(new AuditLogListRequest
            { ActorRole = role, Status = status }).IsValid);
    }

    [Fact]
    public void Search_and_date_ranges_are_bounded()
    {
        var validator = new AuditLogListRequestValidator();
        Assert.False(validator.Validate(new AuditLogListRequest { Search = new string('x', 201) }).IsValid);
        Assert.True(validator.Validate(new AuditLogListRequest { Search = new string('x', 200) }).IsValid);
        Assert.False(validator.Validate(new AuditLogListRequest
            { From = DateTimeOffset.UtcNow, To = DateTimeOffset.UtcNow.AddDays(-1) }).IsValid);
    }
}
