using System.Text.Json;

namespace AgriSage.Application.Features.Notifications;

public static class BusinessNotificationPolicy
{
    public static bool Supports(string action) => action is "ORDER_PLACED" or "ORDER_CONFIRMED" or "ORDER_CANCELLED"
        or "ORDER_PREPARING_STARTED" or "ORDER_MARKED_READY" or "ORDER_PICKED_UP"
        or "PAYMENT_RECEIVED" or "DEBT_PAYMENT_CONFIRMED" or "DELIVERY_ASSIGNED"
        or "DELIVERY_DISPATCHED" or "DELIVERY_ATTEMPT_COMPLETED" or "DELIVERY_CANCELLED"
        or "DELIVERY_CREATED" or "PAYMENT_FAILED" or "DEBT_PAYMENT_REJECTED"
        or "DEBT_CREATED" or "DEBT_MANUAL_ENTRY" or "CUSTOMER_CREDIT_LIMIT_CHANGED"
        or "DEBT_DISPUTE"
        or "RETURN_REQUESTED" or "RETURN_APPROVED" or "RETURN_REJECTED" or "RETURN_CANCELLED"
        or "RETURN_INSPECTION_COMPLETED" or "REFUND_PENDING" or "REFUND_COMPLETED"
        or "REFUND_FAILED" or "REFUND_CANCELLED" or "ORDER_CREATED" or "COUNTER_SALE_COMPLETED"
        or "ORDER_ITEM_REMAINING_CANCELLED";

    public static (string Type, string Title, string Message) Describe(string action, string? newValues = null,
        bool debtPayment = false) => action switch
    {
        "ORDER_PLACED" => ("ORDER_PLACED", "Đơn hàng mới chờ xác nhận", "Nông dân vừa đặt đơn hàng mới. Vui lòng kiểm tra và xác nhận đơn hàng."),
        "ORDER_CREATED" => ("ORDER_PLACED", "Đơn hàng mới chờ xác nhận", "Một đơn hàng mới đã được tạo tại cửa hàng và đang chờ xác nhận."),
        "COUNTER_SALE_COMPLETED" => ("ORDER_STATUS_CHANGED", "Đơn bán tại quầy đã hoàn tất", "Đơn bán tại quầy đã được thanh toán và bàn giao hàng."),
        "ORDER_ITEM_REMAINING_CANCELLED" => ("ORDER_STATUS_CHANGED", "Đã hủy phần hàng còn lại", "Phần hàng còn lại của đơn hàng đã được hủy. Vui lòng xem chi tiết đơn hàng."),
        "ORDER_CONFIRMED" => ("ORDER_STATUS_CHANGED", "Đơn hàng đã được xác nhận", "Đơn hàng đã được xác nhận. Vui lòng xem chi tiết đơn hàng."),
        "ORDER_CANCELLED" => ("ORDER_STATUS_CHANGED", "Đơn hàng đã được hủy", "Đơn hàng đã được hủy. Vui lòng xem chi tiết đơn hàng."),
        "ORDER_PREPARING_STARTED" => ("ORDER_STATUS_CHANGED", "Đơn hàng đang được chuẩn bị", "Cửa hàng đang chuẩn bị hàng cho bạn."),
        "ORDER_MARKED_READY" => ("ORDER_STATUS_CHANGED", "Đơn hàng đã sẵn sàng", "Đơn hàng của bạn đã sẵn sàng để nhận hoặc giao."),
        "ORDER_PICKED_UP" => ("ORDER_STATUS_CHANGED", "Đã ghi nhận nhận hàng", "Cửa hàng đã ghi nhận lần nhận hàng của bạn."),
        "PAYMENT_RECEIVED" or "DEBT_PAYMENT_CONFIRMED" when debtPayment =>
            ("DEBT_PAYMENT_CONFIRMED", "Đã xác nhận thanh toán công nợ", "Khoản thanh toán công nợ đã được xác nhận."),
        "PAYMENT_RECEIVED" or "DEBT_PAYMENT_CONFIRMED" => ("PAYMENT_CONFIRMED", "Đã xác nhận thanh toán", "Khoản thanh toán đã được xác nhận."),
        "PAYMENT_FAILED" or "DEBT_PAYMENT_REJECTED" => ("PAYMENT_FAILED", "Thanh toán không thành công", "Khoản thanh toán không thành công hoặc bị từ chối. Vui lòng kiểm tra chi tiết thanh toán."),
        "DEBT_CREATED" or "DEBT_MANUAL_ENTRY" => ("DEBT_CREATED", "Có khoản công nợ mới", "Một khoản công nợ mới đã phát sinh. Vui lòng kiểm tra số tiền và hạn thanh toán."),
        "DEBT_DISPUTE" => ("DEBT_DISPUTED", "Nông dân khiếu nại công nợ", "Có khiếu nại mới về khoản công nợ. Vui lòng kiểm tra lý do và xử lý."),
        "DELIVERY_CREATED" => ("DELIVERY_REQUIRED", "Đơn hàng cần giao", "Phiếu giao hàng mới đã được tạo. Vui lòng kiểm tra và phân công giao hàng."),
        "DELIVERY_ASSIGNED" => ("DELIVERY_ASSIGNED", "Bạn được giao một phiếu giao hàng", "Vui lòng kiểm tra phiếu giao hàng vừa được phân công."),
        "DELIVERY_DISPATCHED" => ("ORDER_STATUS_CHANGED", "Đơn hàng đang được giao", "Nhân viên đang giao hàng cho bạn."),
        "DELIVERY_CANCELLED" => ("ORDER_STATUS_CHANGED", "Phiếu giao hàng đã được hủy", "Phiếu giao hàng của bạn có thay đổi. Vui lòng xem chi tiết."),
        "DELIVERY_ATTEMPT_COMPLETED" when SnapshotStatus(newValues) == "FAILED" =>
            ("DELIVERY_FAILED", "Giao hàng thất bại", "Lần giao hàng không thành công. Vui lòng xem chi tiết và phương án giao lại."),
        "DELIVERY_ATTEMPT_COMPLETED" when SnapshotStatus(newValues) == "PARTIAL_SUCCESS" =>
            ("DELIVERY_PARTIAL", "Đã giao một phần đơn hàng", "Một phần hàng đã được giao. Vui lòng kiểm tra số lượng còn lại."),
        "DELIVERY_ATTEMPT_COMPLETED" when SnapshotStatus(newValues) == "SUCCESS" =>
            ("DELIVERY_COMPLETED", "Giao hàng thành công", "Lần giao hàng đã hoàn tất thành công. Vui lòng xem chi tiết đơn hàng."),
        "DELIVERY_ATTEMPT_COMPLETED" => ("DELIVERY_COMPLETED", "Đã cập nhật kết quả giao hàng", "Vui lòng xem số lượng đã giao và hàng còn lại trong phiếu giao hàng."),
        "CUSTOMER_CREDIT_LIMIT_CHANGED" => ("CREDIT_LIMIT_CHANGED", "Hạn mức tín dụng đã thay đổi", "Cửa hàng đã cập nhật hạn mức tín dụng của bạn."),
        "RETURN_REQUESTED" => ("RETURN_REQUESTED", "Có yêu cầu trả hàng mới", "Vui lòng kiểm tra và xử lý yêu cầu trả hàng mới."),
        "RETURN_APPROVED" => ("RETURN_RESULT", "Yêu cầu trả hàng được chấp thuận", "Cửa hàng đã chấp thuận yêu cầu trả hàng. Vui lòng xem hướng dẫn trả hàng."),
        "RETURN_REJECTED" => ("RETURN_RESULT", "Yêu cầu trả hàng bị từ chối", "Cửa hàng đã từ chối yêu cầu trả hàng. Vui lòng xem chi tiết hoặc liên hệ cửa hàng."),
        "RETURN_CANCELLED" => ("RETURN_RESULT", "Yêu cầu trả hàng đã được hủy", "Yêu cầu trả hàng đã được hủy. Vui lòng xem chi tiết đơn hàng."),
        "RETURN_INSPECTION_COMPLETED" => ("RETURN_RESULT", "Đã có kết quả kiểm tra hàng trả", "Cửa hàng đã kiểm tra hàng trả và cập nhật kết quả xử lý."),
        "REFUND_PENDING" => ("REFUND_REQUESTED", "Có yêu cầu hoàn tiền", "Có khoản hoàn tiền đang chờ xử lý. Vui lòng kiểm tra yêu cầu hoàn tiền."),
        "REFUND_COMPLETED" => ("REFUND_RESULT", "Hoàn tiền thành công", "Khoản hoàn tiền đã được xác nhận hoàn tất. Vui lòng kiểm tra chi tiết đơn hàng."),
        "REFUND_FAILED" => ("REFUND_RESULT", "Hoàn tiền không thành công", "Khoản hoàn tiền chưa thực hiện thành công. Vui lòng xem chi tiết hoặc liên hệ cửa hàng."),
        "REFUND_CANCELLED" => ("REFUND_RESULT", "Yêu cầu hoàn tiền đã được hủy", "Yêu cầu hoàn tiền đã được hủy. Vui lòng kiểm tra chi tiết đơn hàng."),
        _ => throw new InvalidOperationException("Unsupported business notification.")
    };

    private static string? SnapshotStatus(string? values)
    {
        if (values is null) return null;
        using var document = JsonDocument.Parse(values);
        return document.RootElement.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String
            ? status.GetString() : null;
    }
}
