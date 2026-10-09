namespace AgriSage.Application.Features.Notifications;

public static class BusinessNotificationPolicy
{
    public static bool Supports(string action) => action is "ORDER_CONFIRMED" or "ORDER_CANCELLED"
        or "ORDER_PREPARING_STARTED" or "ORDER_MARKED_READY" or "ORDER_PICKED_UP"
        or "PAYMENT_RECEIVED" or "DEBT_PAYMENT_CONFIRMED" or "DELIVERY_ASSIGNED"
        or "DELIVERY_DISPATCHED" or "DELIVERY_ATTEMPT_COMPLETED" or "DELIVERY_CANCELLED"
        or "CUSTOMER_CREDIT_LIMIT_CHANGED";

    public static (string Type, string Title, string Message) Describe(string action) => action switch
    {
        "ORDER_CONFIRMED" => ("ORDER_STATUS_CHANGED", "Đơn hàng đã được xác nhận", "Cửa hàng đã xác nhận đơn hàng của bạn."),
        "ORDER_CANCELLED" => ("ORDER_STATUS_CHANGED", "Đơn hàng đã được hủy", "Đơn hàng của bạn đã được hủy. Bạn có thể xem chi tiết đơn hàng."),
        "ORDER_PREPARING_STARTED" => ("ORDER_STATUS_CHANGED", "Đơn hàng đang được chuẩn bị", "Cửa hàng đang chuẩn bị hàng cho bạn."),
        "ORDER_MARKED_READY" => ("ORDER_STATUS_CHANGED", "Đơn hàng đã sẵn sàng", "Đơn hàng của bạn đã sẵn sàng để nhận hoặc giao."),
        "ORDER_PICKED_UP" => ("ORDER_STATUS_CHANGED", "Đã ghi nhận nhận hàng", "Cửa hàng đã ghi nhận lần nhận hàng của bạn."),
        "PAYMENT_RECEIVED" or "DEBT_PAYMENT_CONFIRMED" => ("PAYMENT_CONFIRMED", "Đã xác nhận thanh toán", "Khoản thanh toán của bạn đã được xác nhận."),
        "DELIVERY_ASSIGNED" => ("DELIVERY_ASSIGNED", "Bạn được giao một phiếu giao hàng", "Vui lòng kiểm tra phiếu giao hàng vừa được phân công."),
        "DELIVERY_DISPATCHED" => ("ORDER_STATUS_CHANGED", "Đơn hàng đang được giao", "Nhân viên đang giao hàng cho bạn."),
        "DELIVERY_CANCELLED" => ("ORDER_STATUS_CHANGED", "Phiếu giao hàng đã được hủy", "Phiếu giao hàng của bạn có thay đổi. Vui lòng xem chi tiết."),
        "DELIVERY_ATTEMPT_COMPLETED" => ("DELIVERY_COMPLETED", "Đã cập nhật kết quả giao hàng", "Bạn có thể xem số lượng đã giao và hàng còn lại trong phiếu giao hàng."),
        "CUSTOMER_CREDIT_LIMIT_CHANGED" => ("CREDIT_LIMIT_CHANGED", "Hạn mức tín dụng đã thay đổi", "Cửa hàng đã cập nhật hạn mức tín dụng của bạn."),
        _ => throw new InvalidOperationException("Unsupported business notification.")
    };
}
