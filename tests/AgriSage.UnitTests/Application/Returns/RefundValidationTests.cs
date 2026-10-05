using AgriSage.Application.Features.Returns;

namespace AgriSage.UnitTests.Application.Returns;

public class RefundValidationTests
{
    [Fact]
    public void Refund_money_method_and_optional_payment_are_validated()
    {
        var validator = new RefundRequestValidator();
        foreach (var method in new[] { "CASH", "bank_transfer", "OTHER_EXTERNAL" })
            Assert.True(validator.Validate(new RefundRequest(method, 100.01m)).IsValid);
        foreach (var amount in new[] { 0m, -1m, 1.001m, 10000000000000000m })
            Assert.False(validator.Validate(new RefundRequest("CASH", amount)).IsValid);
        Assert.False(validator.Validate(new RefundRequest("PAYOS", 100)).IsValid);
        Assert.False(validator.Validate(new RefundRequest("CASH", 100, Guid.Empty)).IsValid);
        Assert.False(validator.Validate(new RefundRequest("CASH", 100, ExternalReference: new string('r', 201))).IsValid);
        Assert.False(new OrderRefundRequestValidator().Validate(new OrderRefundRequest(Guid.Empty, "CASH", 100)).IsValid);
    }
    [Fact]
    public void Completion_failure_and_cancellation_validate_notes_and_proof()
    {
        var validator = new CompleteRefundRequestValidator();
        Assert.True(validator.Validate(new CompleteRefundRequest()).IsValid);
        Assert.True(validator.Validate(new CompleteRefundRequest("ref", "https://example.test/proof.png", "done")).IsValid);
        Assert.False(validator.Validate(new CompleteRefundRequest(ProofFileUrl: "http://example.test/proof.png")).IsValid);
        Assert.False(validator.Validate(new CompleteRefundRequest(Note: new string('n', 1001))).IsValid);
        Assert.False(new FailRefundRequestValidator().Validate(new FailRefundRequest(new string('n', 1001))).IsValid);
        Assert.False(new CancelRefundRequestValidator().Validate(new CancelRefundRequest(" ")).IsValid);
    }
}
