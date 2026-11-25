using AgriSage.Application.Features.Exchanges;
using AgriSage.Application.Features.Returns;

namespace AgriSage.UnitTests.Application.Exchanges;

public class ExchangeValidationTests
{
    [Fact]
    public void Invalid_sources_duplicate_replacements_and_empty_reason_are_rejected()
    {
        var id = Guid.NewGuid();
        var request = new CreateExchangeRequest(id, Guid.NewGuid(), "Reason", [new(id, 1, "OTHER", OriginalStockMovementItemId: id)], [new(id, id, 1)]);
        var validator = new CreateExchangeRequestValidator(); Assert.True(validator.Validate(request).IsValid);
        Assert.False(validator.Validate(request with { Reason = " " }).IsValid);
        Assert.False(validator.Validate(request with { RequestKey = Guid.Empty }).IsValid);
        Assert.False(validator.Validate(request with { Replacements = [new(id,id,1), new(id,id,1)] }).IsValid);
        Assert.False(validator.Validate(request with { Items = [new(id, 1, "OTHER")] }).IsValid);
        Assert.False(validator.Validate(request with { Replacements = [null!] }).IsValid);
    }
    [Fact]
    public void Settlement_rejects_fractional_money_bad_lots_and_nonphysical_refund_method()
    {
        var id = Guid.NewGuid(); var validator = new CompleteExchangeRequestValidator();
        var valid = new CompleteExchangeRequest(-10m, "Receipt", [new(id,id,1)]);
        Assert.True(validator.Validate(valid).IsValid);
        Assert.False(validator.Validate(valid with { ConfirmedDifference = decimal.MinValue }).IsValid);
        Assert.False(validator.Validate(valid with { ConfirmedDifference = 0.001m }).IsValid);
        Assert.False(validator.Validate(valid with { Lots = [new(id,id,1), new(id,id,1)] }).IsValid);
        Assert.False(validator.Validate(valid with { Lots = [null!] }).IsValid);
        Assert.False(new RefundRequestValidator().Validate(new RefundRequest("EXCHANGE_CREDIT", 10m)).IsValid);
    }
}
