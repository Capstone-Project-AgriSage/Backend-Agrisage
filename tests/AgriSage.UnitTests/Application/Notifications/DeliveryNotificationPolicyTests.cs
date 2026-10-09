using AgriSage.Application.Features.Notifications;

namespace AgriSage.UnitTests.Application.Notifications;

public sealed class DeliveryNotificationPolicyTests
{
    [Theory]
    [InlineData("FAILED", "DELIVERY_FAILED", "Giao hàng thất bại")]
    [InlineData("PARTIAL_SUCCESS", "DELIVERY_PARTIAL", "Đã giao một phần đơn hàng")]
    [InlineData("SUCCESS", "DELIVERY_COMPLETED", "Giao hàng thành công")]
    public void Notification_reports_the_recorded_attempt_result(string result, string type, string title)
    {
        var description = BusinessNotificationPolicy.Describe("DELIVERY_ATTEMPT_COMPLETED", "{\"status\":\"" + result + "\"}");
        Assert.Equal(type, description.Type);
        Assert.Equal(title, description.Title);
    }

    [Fact]
    public void Legacy_attempt_without_a_status_uses_a_neutral_update()
    {
        var description = BusinessNotificationPolicy.Describe("DELIVERY_ATTEMPT_COMPLETED");
        Assert.Equal("Đã cập nhật kết quả giao hàng", description.Title);
    }

    [Fact]
    public void Debt_dispute_is_a_supported_owner_notification()
    {
        Assert.True(BusinessNotificationPolicy.Supports("DEBT_DISPUTE"));

        var description = BusinessNotificationPolicy.Describe("DEBT_DISPUTE");

        Assert.Equal("DEBT_DISPUTED", description.Type);
        Assert.Equal("Nông dân khiếu nại công nợ", description.Title);
    }
}
