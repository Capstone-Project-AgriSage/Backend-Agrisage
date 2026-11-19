using AgriSage.Application.Features.Notifications;

namespace AgriSage.UnitTests.Application.Notifications;

// Debt, payment and credit-limit notifications name their figures when the dispatcher has them (NotificationFacts); the type
// and title never change, and a missing figure falls back to the generic text.
public sealed class DebtNotificationTextTests
{
    private static readonly DateOnly Due = new(2026, 12, 15);

    [Fact]
    public void A_new_debt_names_its_amount_order_due_date_and_entry_number()
    {
        var text = BusinessNotificationPolicy.Describe("DEBT_CREATED",
            facts: new NotificationFacts(EntryNumber: "DE-0007", OrderNumber: "DH-0012", Amount: 2_000_000m, DueDate: Due));

        Assert.Equal(("DEBT_CREATED", "Có khoản công nợ mới",
            "Bạn có khoản nợ mới 2.000.000 đ của đơn DH-0012, hạn trả 15/12/2026 (mã DE-0007). Vui lòng xem chi tiết công nợ."), text);
    }

    [Fact]
    public void A_manual_debt_without_an_order_names_amount_due_date_and_entry_number()
    {
        var text = BusinessNotificationPolicy.Describe("DEBT_MANUAL_ENTRY",
            facts: new NotificationFacts(EntryNumber: "DE-0008", Amount: 500_000m, DueDate: Due));

        Assert.Equal("Bạn có khoản nợ mới 500.000 đ, hạn trả 15/12/2026 (mã DE-0008). Vui lòng xem chi tiết công nợ.", text.Message);
    }

    [Fact]
    public void Payments_name_the_amount_and_the_payer_or_the_order()
    {
        var debt = BusinessNotificationPolicy.Describe("PAYMENT_RECEIVED", debtPayment: true,
            facts: new NotificationFacts(Amount: 1_000_000m, FarmerName: "Nguyễn Văn A"));
        var order = BusinessNotificationPolicy.Describe("PAYMENT_RECEIVED",
            facts: new NotificationFacts(OrderNumber: "DH-0012", Amount: 350_000m, FarmerName: "Nguyễn Văn A"));

        Assert.Equal(("DEBT_PAYMENT_CONFIRMED", "Đã nhận 1.000.000 đ trả nợ của Nguyễn Văn A. Vui lòng xem số nợ còn lại."), (debt.Type, debt.Message));
        Assert.Equal(("PAYMENT_CONFIRMED", "Đã nhận 350.000 đ cho đơn DH-0012. Vui lòng xem chi tiết thanh toán."), (order.Type, order.Message));
    }

    [Fact]
    public void A_dispute_tells_the_store_who_which_debt_how_much_and_why()
    {
        var text = BusinessNotificationPolicy.Describe("DEBT_DISPUTE", facts: new NotificationFacts(EntryNumber: "DE-0007",
            OrderNumber: "DH-0012", Outstanding: 1_500_000m, FarmerName: "Nguyễn Văn A", Reason: "  Tôi chỉ lấy 1 chai. "));

        Assert.Equal(("DEBT_DISPUTED", "Nông dân khiếu nại công nợ",
            "Nguyễn Văn A khiếu nại khoản nợ của đơn DH-0012 (1.500.000 đ). Lý do: Tôi chỉ lấy 1 chai. Vui lòng kiểm tra và xử lý."), text);
    }

    [Fact]
    public void A_long_dispute_reason_is_shortened()
    {
        var text = BusinessNotificationPolicy.Describe("DEBT_DISPUTE",
            facts: new NotificationFacts(EntryNumber: "DE-0007", Outstanding: 1m, Reason: new string('a', 300)));

        Assert.Contains($"Lý do: {new string('a', 117)}....", text.Message);
        Assert.StartsWith("Nông dân khiếu nại khoản nợ DE-0007 (1 đ).", text.Message);
    }

    [Theory]
    [InlineData("DEBT_ROLLOVER")]
    [InlineData("DEBT_CHANGE_DUE_DATE")]
    public void A_new_due_date_is_named(string action)
    {
        var text = BusinessNotificationPolicy.Describe(action, facts: new NotificationFacts(OrderNumber: "DH-0012", DueDate: Due));

        Assert.Equal(("DEBT_DUE_DATE_CHANGED", "Hạn trả khoản nợ của đơn DH-0012 đã đổi sang ngày 15/12/2026. Vui lòng xem chi tiết công nợ."),
            (text.Type, text.Message));
    }

    [Fact]
    public void Reduction_cancellation_and_kept_dispute_name_the_debt_and_what_is_left()
    {
        var facts = new NotificationFacts(EntryNumber: "DE-0007", OrderNumber: "DH-0012", Outstanding: 1_300_000m);

        Assert.Equal("Cửa hàng đã giảm khoản nợ của đơn DH-0012, số còn nợ 1.300.000 đ. Vui lòng xem chi tiết công nợ.",
            BusinessNotificationPolicy.Describe("DEBT_ADJUST", facts: facts).Message);
        Assert.Equal("Cửa hàng đã xóa khoản nợ của đơn DH-0012. Bạn không còn phải trả khoản này.",
            BusinessNotificationPolicy.Describe("DEBT_CANCEL", facts: facts with { Outstanding = 0m }).Message);
        Assert.Equal("Cửa hàng đã xem xét khiếu nại và giữ nguyên khoản nợ của đơn DH-0012 (1.300.000 đ). Vui lòng liên hệ cửa hàng nếu cần.",
            BusinessNotificationPolicy.Describe("DEBT_KEEP", facts: facts).Message);
    }

    [Fact]
    public void A_credit_limit_change_names_the_old_and_new_limits()
    {
        var changed = BusinessNotificationPolicy.Describe("CUSTOMER_CREDIT_LIMIT_CHANGED",
            facts: new NotificationFacts(FarmerName: "Nguyễn Văn A", CreditLimit: 25_000_000m, PreviousCreditLimit: 20_000_000m));
        var first = BusinessNotificationPolicy.Describe("CUSTOMER_CREDIT_LIMIT_CHANGED", facts: new NotificationFacts(CreditLimit: 5_000_000m));

        Assert.Equal(("CREDIT_LIMIT_CHANGED", "Hạn mức tín dụng của Nguyễn Văn A đã đổi từ 20.000.000 đ thành 25.000.000 đ."), (changed.Type, changed.Message));
        Assert.Equal("Hạn mức tín dụng hiện là 5.000.000 đ.", first.Message);
    }

    [Fact]
    public void Missing_figures_and_other_actions_keep_the_generic_text()
    {
        var generic = BusinessNotificationPolicy.Describe("DEBT_CREATED");

        Assert.Equal(generic, BusinessNotificationPolicy.Describe("DEBT_CREATED", facts: new NotificationFacts(Amount: 1m)));
        Assert.Equal(generic, BusinessNotificationPolicy.Describe("DEBT_CREATED", facts: new NotificationFacts()));
        Assert.Equal(BusinessNotificationPolicy.Describe("ORDER_CONFIRMED"),
            BusinessNotificationPolicy.Describe("ORDER_CONFIRMED", facts: new NotificationFacts(Amount: 1m, OrderNumber: "DH-1")));
        Assert.Equal("Một khoản công nợ mới đã phát sinh. Vui lòng kiểm tra số tiền và hạn thanh toán.", generic.Message);
    }
}
